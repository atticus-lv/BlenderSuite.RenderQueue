#!/usr/bin/env bash
set -euo pipefail

tag_name="${1:?Usage: generate-release-notes.sh <tag> [output-file]}"
output_file="${2:-release-notes.md}"

repo_url="https://github.com/${GITHUB_REPOSITORY:-atticus-lv/BlenderSuite.RenderQueue}"
if [[ -z "${GITHUB_REPOSITORY:-}" ]]; then
  origin_url="$(git config --get remote.origin.url || true)"
  if [[ "$origin_url" =~ ^git@github.com:(.+)\.git$ ]]; then
    repo_url="https://github.com/${BASH_REMATCH[1]}"
  elif [[ "$origin_url" =~ ^https://github.com/(.+)\.git$ ]]; then
    repo_url="https://github.com/${BASH_REMATCH[1]}"
  elif [[ "$origin_url" =~ ^https://github.com/(.+)$ ]]; then
    repo_url="https://github.com/${BASH_REMATCH[1]}"
  fi
fi

previous_tag="$(git describe --tags --abbrev=0 "${tag_name}^" 2>/dev/null || true)"
if [[ -n "$previous_tag" ]]; then
  log_range="${previous_tag}..${tag_name}"
else
  log_range="$tag_name"
fi

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
commit_prefix_pattern='^([[:alpha:]]+(/[[:alpha:]]+)?)(\([^)]+\))?:[[:space:]]*(.+)$'

trim() {
  local value="$*"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

normalize_type() {
  case "$1" in
    feat|fix|change|improve|refactor|cleanup|docs|ci/cd)
      printf '%s' "$1"
      ;;
    *)
      printf 'other'
      ;;
  esac
}

append_change() {
  local type="$1"
  local message="$2"
  local short_sha="$3"
  local type_file="${type//\//_}"

  message="$(trim "$message")"
  if [[ -z "$message" ]]; then
    return
  fi

  printf -- '- %s (%s)\n' "$message" "$short_sha" >> "$tmp_dir/${type_file}.md"
}

while IFS=$'\t' read -r subject short_sha; do
  [[ -z "$subject" ]] && continue

  IFS=';' read -ra parts <<< "$subject"
  for part in "${parts[@]}"; do
    part="$(trim "$part")"
    [[ -z "$part" ]] && continue

    if [[ "$part" =~ $commit_prefix_pattern ]]; then
      change_type="$(normalize_type "${BASH_REMATCH[1],,}")"
      append_change "$change_type" "${BASH_REMATCH[4]}" "$short_sha"
    else
      append_change "other" "$part" "$short_sha"
    fi
  done
done < <(git log --format='%s%x09%h' "$log_range")

section_types=(feat fix change improve refactor cleanup docs ci/cd other)
section_titles=("Features" "Fixes" "Changes" "Improvements" "Refactors" "Cleanup" "Documentation" "CI/CD" "Other")

{
  echo "## Changelog"
  echo

  has_changes=0
  for index in "${!section_types[@]}"; do
    type="${section_types[$index]}"
    section_file="$tmp_dir/${type//\//_}.md"
    if [[ -s "$section_file" ]]; then
      echo "### ${section_titles[$index]}"
      cat "$section_file"
      echo
      has_changes=1
    fi
  done

  if [[ "$has_changes" -eq 0 ]]; then
    echo "- No categorized changes."
    echo
  fi

  if [[ -n "$previous_tag" ]]; then
    echo "**Full Changelog**: ${repo_url}/compare/${previous_tag}...${tag_name}"
  else
    echo "**Full Changelog**: ${repo_url}/releases/tag/${tag_name}"
  fi
} > "$output_file"
