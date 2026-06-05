import type { DownloadPlatform, FeatureItem, NavItem, WorkflowStep } from '../types/site'

export type SiteLocale = 'zh' | 'en'

export type HomeContent = {
  navItems: NavItem[]
  hero: {
    eyebrow: string
    title: string
    description: string
    primaryCta: string
    secondaryCta: string
  }
  preview: {
    prompt: string
    note: string
    loading: string
  }
  sections: {
    advantages: string
    workflow: string
    features: string
  }
  advantages: FeatureItem[]
  workflowSteps: WorkflowStep[]
  coreCapabilities: FeatureItem[]
  download: {
    title: string
    versionLabel: string
    releaseLink: string
    platformsLabel: string
  }
  languageToggleLabel: string
}

export const supportedLocales = ['zh', 'en'] as const

export const siteConfig = {
  productName: 'Blender Suite: Render Queue',
  version: '0.6.1.0',
  githubUrl: 'https://github.com/atticus-lv/BlenderSuite.RenderQueue',
  releaseUrl: 'https://github.com/atticus-lv/BlenderSuite.RenderQueue/releases',
}

export const siteContent: Record<SiteLocale, HomeContent> = {
  zh: {
    navItems: [
      { label: '下载', href: '#download' },
    ],
    hero: {
      eyebrow: 'Blender Suite',
      title: 'Render Queue',
      description: '基于 Avalonia 构建的跨平台原生高性能 Blender 批量渲染工具。',
      primaryCta: '试试',
      secondaryCta: '直接下载',
    },
    preview: {
      prompt: '试试交互手感',
      note: '此内嵌页面由项目编译为 WASM，交互效果与桌面端一致。',
      loading: '正在加载预览...',
    },
    sections: {
      advantages: '优势',
      workflow: '工作流',
      features: '核心功能',
    },
    advantages: [
      {
        title: '开源',
        description: '遵循 AGPL-3.0 协议，源码、构建流程和发布记录均可在 GitHub 查看。',
      },
      {
        title: '高性能',
        description: '.NET 10 + Avalonia 12，通过 AOT 提供原生级性能。启动快速、内存占用低。',
      },
      {
        title: '开箱即用',
        description: '一键安装，自动搜寻本机 Blender 并安装扩展插件，在 Blender 内一键提交，即可开始渲染。',
      },
    ],
    workflowSteps: [
      {
        title: '提交场景',
        body: '通过 Blender 扩展，自动选择场景与帧范围，生成桌面端渲染任务。',
      },
      {
        title: '管理队列',
        body: '调整任务顺序、启停任务、查看运行状态，并按任务覆写渲染参数。',
      },
      {
        title: '处理输出',
        body: '预览序列帧，一键合成或在渲染后关闭主机。',
      },
    ],
    coreCapabilities: [
      {
        title: '任务队列',
        description: '支持任务排序、暂停、继续、禁用和覆写。',
      },
      {
        title: '多场景',
        description: '支持 Blender 5.0 剪辑序列，可读取多场景镜头并生成拼接渲染任务。',
      },
      {
        title: '序列帧处理',
        description: '支持路径表达式解析、序列帧预览和一键合成视频。',
      },
      {
        title: '运行监控',
        description: '展示队列状态、当前帧、任务进度、硬件占用和底层日志。',
      },
      {
        title: '完成后动作',
        description: '渲染完成后可自动关机或睡眠。',
      },
      {
        title: 'Windows / macOS',
        description: '桌面端覆盖 Windows 与 macOS，支持 x64/arm。',
      },
    ],
    download: {
      title: '下载 Blender Suite: Render Queue',
      versionLabel: '当前版本',
      releaseLink: 'GitHub Releases',
      platformsLabel: '可用平台',
    },
    languageToggleLabel: '语言切换',
  },
  en: {
    navItems: [
      { label: 'Download', href: '#download' },
    ],
    hero: {
      eyebrow: 'Blender Suite',
      title: 'Render Queue',
      description: 'A native, high-performance cross-platform Blender batch rendering tool built with Avalonia.',
      primaryCta: 'Try it',
      secondaryCta: 'Download',
    },
    preview: {
      prompt: 'Try the interaction',
      note: 'This embedded page is compiled to WASM from the project and keeps the same interaction feel as the desktop app.',
      loading: 'Loading preview...',
    },
    sections: {
      advantages: 'Advantages',
      workflow: 'Workflow',
      features: 'Core Features',
    },
    advantages: [
      {
        title: 'Open source',
        description: 'Licensed under AGPL-3.0, with source code, build flow, and release history available on GitHub.',
      },
      {
        title: 'Native performance',
        description: '.NET 10 + Avalonia 12 with AOT for fast startup, responsive interaction, and low memory use.',
      },
      {
        title: 'Ready to use',
        description: 'Install once, find the local Blender app, add the extension, and submit render jobs from Blender.',
      },
    ],
    workflowSteps: [
      {
        title: 'Submit scenes',
        body: 'Use the Blender extension to choose scenes and frame ranges, then create desktop render jobs.',
      },
      {
        title: 'Manage the queue',
        body: 'Reorder jobs, pause or resume tasks, inspect runtime status, and override render settings per task.',
      },
      {
        title: 'Process output',
        body: 'Preview image sequences, compose videos, or shut down the host after rendering finishes.',
      },
    ],
    coreCapabilities: [
      {
        title: 'Task queue',
        description: 'Sort, pause, resume, disable, and override render tasks.',
      },
      {
        title: 'Multiple scenes',
        description: 'Supports Blender 5.0 sequencer workflows and generates stitched render jobs from multiple shots.',
      },
      {
        title: 'Frame pipeline',
        description: 'Parse output paths, preview image sequences, and compose video in one flow.',
      },
      {
        title: 'Live monitor',
        description: 'Track queue state, current frame, task progress, hardware usage, and low-level logs.',
      },
      {
        title: 'Post-render actions',
        description: 'Shut down or sleep the machine after rendering completes.',
      },
      {
        title: 'Windows / macOS',
        description: 'Desktop builds cover Windows and macOS across x64/arm.',
      },
    ],
    download: {
      title: 'Download Blender Suite: Render Queue',
      versionLabel: 'Version',
      releaseLink: 'GitHub Releases',
      platformsLabel: 'Available platforms',
    },
    languageToggleLabel: 'Language',
  },
}

export const downloadPlatforms: DownloadPlatform[] = [
  {
    id: 'windows',
    label: 'Windows',
    icon: 'windows',
    status: 'available',
    href: '#windows-download',
  },
  {
    id: 'macos',
    label: 'macOS',
    icon: 'macos',
    status: 'available',
    href: '#macos-download',
  },
]

export function isSupportedLocale(value: string | undefined): value is SiteLocale {
  return supportedLocales.includes(value as SiteLocale)
}

export function getPreferredLocale(language = navigator.language): SiteLocale {
  return language.toLowerCase().startsWith('zh') ? 'zh' : 'en'
}
