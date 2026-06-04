import type {
  DownloadPlatform,
  FeatureItem,
  FeatureStory,
  NavItem,
  WorkflowStep,
} from '../types/site'

export const siteConfig = {
  productName: 'Blender Render Queue',
  shortDescription:
    '一个面向 Blender 工作流的队列渲染工具，专注于排队渲染、进度可见和结果整理。',
  version: '0.5.8.3',
  primaryCta: {
    label: '下载',
    href: '#download',
  },
  secondaryCta: {
    label: '查看功能亮点',
    href: '#features',
  },
  githubUrl: 'https://github.com/atticus-lv/BlenderSuite.RenderQueue',
  releaseUrl: 'https://github.com/atticus-lv/BlenderSuite.RenderQueue/releases',
}

export const navItems: NavItem[] = [
  { label: '概览', href: '#overview' },
  { label: '工作流', href: '#workflow' },
  { label: '功能', href: '#features' },
  { label: '下载', href: '#download' },
]

export const supportHighlights: FeatureItem[] = [
  {
    eyebrow: 'Queue First',
    title: '让等待中的渲染任务排成队，而不是排在脑子里。',
    description:
      '把多个场景、帧段和输出要求集中到一个工作台里，避免反复打开、反复设置、反复确认。',
  },
  {
    eyebrow: 'Visible Progress',
    title: '在渲染进行时，知道它走到了哪里。',
    description:
      '进度、日志与状态变化都在一个视图里汇总，长任务也能保持可见，不再靠猜测判断是否卡住。',
  },
  {
    eyebrow: 'Output Control',
    title: '结果整理不是收尾动作，而是流程的一部分。',
    description:
      '从帧范围覆写到视频合成，输出后的动作和渲染本身被放进同一条链路里管理。',
  },
]

export const workflowSteps: WorkflowStep[] = [
  {
    title: '导入 Blender 场景',
    body: '把需要排队的 .blend 文件和渲染参数加入工作台，建立一条清晰的任务序列。',
  },
  {
    title: '观察队列与状态',
    body: '在运行过程中实时查看进度、日志、当前任务状态，并在需要时暂停、恢复或调整节奏。',
  },
  {
    title: '集中收拢输出结果',
    body: '渲染完成后继续衔接视频合成与结果整理，把“做完”真正变成“可交付”。',
  },
]

export const featureDepth: FeatureItem[] = [
  {
    eyebrow: 'Render Queue',
    title: '队列渲染',
    description: '把零散任务变成连续流程，让多个场景和多轮导出在一个地方排队执行。',
  },
  {
    eyebrow: 'Scene Overrides',
    title: '场景 / 帧范围覆写',
    description: '在不反复改动源文件的前提下，灵活指定要渲染的区间和参数。',
  },
  {
    eyebrow: 'Control',
    title: '暂停 / 恢复',
    description: '面对长时任务时更从容，能根据机器状态和实际需要调整执行节奏。',
  },
  {
    eyebrow: 'Finish',
    title: '视频合成',
    description: '把输出后的整合工作拉回到主流程里，不让最后一公里再次碎裂。',
  },
  {
    eyebrow: 'Interface',
    title: '双语界面与主题切换',
    description: '中英文界面和深浅主题都在产品里准备好，适合更长期的日常使用。',
  },
  {
    eyebrow: 'Platforms',
    title: '跨平台进展',
    description: '当前主线覆盖 Windows 与 macOS 打包，Linux 与更多配套能力正在补齐。',
  },
]

export const featureStories: FeatureStory[] = [
  {
    id: 'queue',
    eyebrow: 'Queue Control',
    title: '队列渲染、暂停、恢复，不再把流程拆碎在一堆临时动作里。',
    intro:
      '真正的价值不是“能渲染”，而是把多个 Blender 任务收拢进一条可持续运转的链路。',
    body:
      'Blender Render Queue 把添加任务、排队执行、观察进度、暂停恢复和结果收尾放进同一个工作台里。你不需要在多个窗口之间来回确认，也不需要靠记忆维持当前轮次跑到了哪里。',
    points: [
      '多个场景和帧段可以连续排队执行，避免重复打开与重复设置。',
      '运行中的队列可以暂停、恢复或停止，让长任务更符合真实机器状态。',
      '队列状态、任务状态和输出节奏被放在一个视图里，而不是散落在多个工具和目录里。',
    ],
    accentLabel: '从“一个任务”变成“一个流程”',
  },
  {
    id: 'performance',
    eyebrow: 'Native Performance',
    title: '高性能、极速启动、原生桌面体验，让工具像工作台的一部分，而不是负担。',
    intro:
      '这个项目不是一个包着网页壳的控制台，而是为持续使用准备的原生桌面应用。',
    body:
      '从启动速度到界面响应，再到长期打开时的可用性，Blender Render Queue 都以桌面工作流为前提设计。它更接近一个你可以全天挂着的辅助面板，而不是一个偶尔打开的脚本入口。',
    points: [
      '原生 App 形态让启动、切换和日常操作更干脆，适合频繁进入工作流。',
      '核心交互围绕队列、状态、日志和设置展开，没有多余页面层层包裹。',
      '面向多平台打包和发布，产品路径天然指向长期使用，而不是一次性工具脚本。',
    ],
    accentLabel: '做得快，也要开得快、看得稳',
  },
  {
    id: 'extension',
    eyebrow: 'Blender Extension',
    title: '通过 Blender 扩展无缝衔接，让提交任务这一步回到你原本的创作上下文里。',
    intro:
      '最顺手的流程不是离开 Blender 再重新组织，而是在 Blender 内部把任务送进队列。',
    body:
      '项目已经围绕 Blender 扩展和本地提交通道布局，让场景信息、输出设置和后续渲染衔接得更自然。网页端这里不把它说成一个抽象“生态”，而是明确它服务的是无缝提交和更少打断。',
    points: [
      'Blender 扩展负责贴近创作现场，降低从场景到队列的切换成本。',
      '本地提交链路让任务进入主应用时更清晰，不需要再手工重复描述上下文。',
      '桌面端与扩展端各自负责一段流程，但对用户来说是同一条连续工作流。',
    ],
    accentLabel: '从 Blender 内部开始，而不是从外部补救',
  },
]

export const downloadPlatforms: DownloadPlatform[] = [
  {
    id: 'windows',
    label: 'Windows',
    icon: 'windows',
    status: '可用版本',
    href: '#windows-download',
    note: '提供 Windows 安装包，可直接用于本地 Blender 渲染队列工作流。',
  },
  {
    id: 'macos',
    label: 'macOS',
    icon: 'macos',
    status: '可用版本',
    href: '#macos-download',
    note: '提供 macOS 构建，适用于本地 Blender 渲染任务管理。',
  },
]

export const releaseNotes: string[] = [
  '队列渲染工作流已可用，适合把长批次任务集中管理。',
  '支持场景与帧范围覆写、暂停恢复、视频合成等实际工作流能力。',
  '中英文界面与深浅主题已集成，适合长期日常使用。',
]
