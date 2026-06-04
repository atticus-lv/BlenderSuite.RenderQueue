export type NavItem = {
  label: string
  href: string
}

export type FeatureItem = {
  title: string
  description: string
  eyebrow?: string
}

export type WorkflowStep = {
  title: string
  body: string
}

export type DownloadPlatform = {
  id: string
  label: string
  icon: 'windows' | 'macos'
  status: string
  href: string
  note?: string
}

export type FeatureStory = {
  id: string
  eyebrow: string
  title: string
  intro: string
  body: string
  points: string[]
  accentLabel: string
}
