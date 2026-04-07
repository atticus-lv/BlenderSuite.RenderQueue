# macOS AOT Known Issues

This list is for evidence tracking only. Do not use the AOT app as the primary baseline for Blender submission debugging until these items are closed.

## Current confirmed issues

1. Native AOT builds still emit trim/AOT warnings around `System.Text.Json` usage in multiple services.
2. AOT app instances have produced repeated macOS crash reports under `~/Library/Logs/DiagnosticReports/BlenderRenderQueue-*.ips`.
3. Crash signatures observed include:
   - `UnhandledExceptionFailFastViaClasslib`
   - `RhFailFast`
   - app exit / teardown paths involving Avalonia native dispatcher and reverse P/Invoke attachment
4. Submission path stability under AOT is not yet trustworthy enough for business-logic regression triage.

## Investigation notes

- Validate behavior first with the non-AOT package.
- Only triage AOT-only failures after non-AOT behavior is confirmed.
- Keep a matching `.dSYM` set under `Install/macOS/symbols/aot/<rid>/` for any AOT build used in testing.
