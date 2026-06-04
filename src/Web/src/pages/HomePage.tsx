import { motion, Reorder, useDragControls, useReducedMotion } from 'framer-motion'
import type { CSSProperties, MouseEvent, PointerEvent } from 'react'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { downloadPlatforms, releaseNotes, siteConfig } from '../content/site'
import styles from './HomePage.module.css'

const MOCKUP_WIDTH = 1280
const MOCKUP_HEIGHT = 960
const WASM_PREVIEW_SRC = `${import.meta.env.BASE_URL}wasm-preview/index.html?v=20260604-wasm-preview-7`
const BRAND_LOGO_SRC = `${import.meta.env.BASE_URL}branding/logo.png`
const CPU_VALUES = [28, 34, 31, 33, 58, 35, 39, 34, 36, 35, 42, 37, 45, 41]
const GPU_VALUES = [45, 38, 42, 40, 35, 47, 29, 43, 38, 46, 41, 49, 44, 48]

const advantageCards = [
  {
    eyebrow: 'AOT Native',
    title: 'AOT 原生桌面应用',
    description:
      'AOT 编译，原生桌面体验。高性能、低占用、启动快，安装包不到 30 MB。',
  },
  {
    eyebrow: 'Blender Extension',
    title: 'Blender 扩展提交',
    description:
      '在 Blender 中读取场景、帧范围、输出路径和渲染配置，直接提交到桌面端队列。',
  },
  {
    eyebrow: 'Queue Control',
    title: '队列控制',
    description:
      '支持断点继续渲染、暂停、终止、插队和拖拽排序，适合长批次任务调度。',
  },
]

const workflowSteps = [
  {
    title: '提交场景',
    body: '通过 Blender 扩展选择场景与帧范围，生成桌面端渲染任务。',
  },
  {
    title: '管理队列',
    body: '调整任务顺序、启停任务、查看运行状态，并按任务覆写渲染参数。',
  },
  {
    title: '处理输出',
    body: '监控帧进度和日志，预览序列帧，并在完成后执行合成或电源动作。',
  },
]

const coreCapabilities = [
  {
    eyebrow: 'Task Management',
    title: '任务队列',
    description: '支持任务排序、暂停、继续、禁用和重新组织，适合批量场景渲染。',
  },
  {
    eyebrow: 'Scene Submit',
    title: '场景提交',
    description: '支持 Blender 5.0 剪辑序列，可读取多场景镜头并生成拼接渲染任务。',
  },
  {
    eyebrow: 'Frame Pipeline',
    title: '序列帧处理',
    description: '提供序列帧预览、输出整理和视频合成相关流程。',
  },
  {
    eyebrow: 'Live Monitor',
    title: '运行监控',
    description: '展示队列状态、当前帧、任务进度、硬件占用和底层日志。',
  },
  {
    eyebrow: 'Post Render',
    title: '完成后动作',
    description: '支持渲染完成后执行合成、整理输出、关机或睡眠等动作。',
  },
  {
    eyebrow: 'Cross Platform',
    title: 'Windows / macOS',
    description: '桌面端覆盖 Windows 与 macOS，按本地渲染环境组织配置。',
  },
]

type QueueItem = {
  id: string
  fileName: string
  scene: string
  range: string
  enabled: boolean
  status: 'waiting' | 'running' | 'disabled'
  progress?: number
}

const iconPaths = {
  blenderSoftware:
    'M12.58,3.12V3.13C12.27,3.13 11.96,3.22 11.71,3.39C11.21,3.74 11.15,4.32 11.6,4.69L14.46,7L5.73,7.03H5.72C5,7.03 4.3,7.5 4.16,8.1C4,8.71 4.5,9.22 5.26,9.22L9.69,9.21L1.76,15.3C1,15.87 0.77,16.82 1.24,17.42C1.72,18.03 2.73,18.03 3.5,17.42L7.8,13.89C7.8,13.89 7.73,14.37 7.74,14.65C7.74,14.94 7.84,15.5 7.97,15.93C8.26,16.86 8.75,17.71 9.43,18.46C10.13,19.23 11,19.85 12,20.29C13.03,20.76 14.17,21 15.34,21C16.5,21 17.65,20.75 18.69,20.28C19.69,19.84 20.55,19.21 21.25,18.44C21.93,17.69 22.42,16.83 22.71,15.91C22.85,15.44 22.94,14.97 23,14.5C23,14.03 23,13.56 22.94,13.09C22.81,12.18 22.5,11.32 22,10.54C21.56,9.83 21,9.2 20.31,8.67V8.67L13.42,3.38C13.19,3.21 12.89,3.12 12.58,3.12M15.34,9.21C16.5,9.21 17.59,9.59 18.46,10.29C18.9,10.65 19.25,11.07 19.5,11.54C19.77,12 19.94,12.55 20,13.11C20.04,13.67 19.96,14.23 19.77,14.77C19.57,15.31 19.25,15.81 18.82,16.26C17.93,17.16 16.69,17.68 15.34,17.68C14,17.68 12.75,17.17 11.86,16.27C11.43,15.83 11.11,15.32 10.91,14.78C10.72,14.25 10.64,13.69 10.69,13.12C10.74,12.56 10.91,12.03 11.17,11.55C11.43,11.08 11.79,10.66 12.23,10.3C13.09,9.59 14.19,9.21 15.34,9.21M15.44,10.61C14.66,10.61 13.94,10.89 13.41,11.34C12.87,11.8 12.5,12.44 12.47,13.18C12.43,13.93 12.73,14.63 13.26,15.15C13.8,15.68 14.58,16 15.44,16C16.3,16 17.07,15.68 17.62,15.15C18.15,14.63 18.45,13.93 18.41,13.18C18.37,12.44 18,11.8 17.47,11.34C16.94,10.89 16.22,10.61 15.44,10.61Z',
  chevronRight: 'M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z',
  listStatus:
    'M16.5 11L13 7.5L14.4 6.1L16.5 8.2L20.7 4L22.1 5.4L16.5 11M11 7H2V9H11V7M21 13.4L19.6 12L17 14.6L14.4 12L13 13.4L15.6 16L13 18.6L14.4 20L17 17.4L19.6 20L21 18.6L18.4 16L21 13.4M11 15H2V17H11V15Z',
  textBoxSearch:
    'M15.5,12C18,12 20,14 20,16.5C20,17.38 19.75,18.21 19.31,18.9L22.39,22L21,23.39L17.88,20.32C17.19,20.75 16.37,21 15.5,21C13,21 11,19 11,16.5C11,14 13,12 15.5,12M15.5,14A2.5,2.5 0 0,0 13,16.5A2.5,2.5 0 0,0 15.5,19A2.5,2.5 0 0,0 18,16.5A2.5,2.5 0 0,0 15.5,14M7,15V17H9C9.14,18.55 9.8,19.94 10.81,21H5C3.89,21 3,20.1 3,19V5C3,3.89 3.89,3 5,3H19A2,2 0 0,1 21,5V13.03C19.85,11.21 17.82,10 15.5,10C14.23,10 13.04,10.37 12.04,11H7V13H10C9.64,13.6 9.34,14.28 9.17,15H7M17,9V7H7V9H17Z',
  settings:
    'M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z',
  arrowRight: 'M4,11V13H16L10.5,18.5L11.92,19.92L19.84,12L11.92,4.08L10.5,5.5L16,11H4Z',
  expandDown: 'm7 10 5 5 5-5Z',
}

function useMockupScale() {
  const ref = useRef<HTMLDivElement>(null)
  const [scale, setScale] = useState(1)

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return undefined

    const updateScale = () => {
      setScale(Math.min(1, element.clientWidth / MOCKUP_WIDTH))
    }

    updateScale()
    const observer = new ResizeObserver(updateScale)
    observer.observe(element)

    return () => observer.disconnect()
  }, [])

  return { ref, scale }
}

function usePreviewAssetAvailable(src: string) {
  const [available, setAvailable] = useState(false)

  useEffect(() => {
    let cancelled = false

    fetch(src, { method: 'HEAD' })
      .then((response) => {
        if (!cancelled) setAvailable(response.ok)
      })
      .catch(() => {
        if (!cancelled) setAvailable(false)
      })

    return () => {
      cancelled = true
    }
  }, [src])

  return available
}

function MaterialIcon({ path, label }: { path: string; label?: string }) {
  return (
    <svg
      aria-hidden={label ? undefined : true}
      aria-label={label}
      className={styles.materialIcon}
      focusable="false"
      viewBox="0 0 24 24"
    >
      <path d={path} />
    </svg>
  )
}

function HandDrawnArrow() {
  return (
    <svg
      aria-hidden="true"
      className={styles.handDrawnArrow}
      focusable="false"
      viewBox="0 0 58 54"
    >
      <path d="M10 7C18 17 28 24 41 29" />
      <path d="M40 29C34 30 29 32 24 36" />
      <path d="M40 29C36 22 34 17 34 12" />
      <path d="M12 10C17 14 22 19 27 24" className={styles.arrowGhost} />
    </svg>
  )
}

const initialItems: QueueItem[] = [
  {
    id: 'untitled',
    fileName: 'Untitled.blend',
    scene: 'Scene',
    range: '1-250',
    enabled: true,
    status: 'waiting',
  },
  {
    id: 'interior',
    fileName: 'Interior_Light.blend',
    scene: 'Camera',
    range: '1-180',
    enabled: true,
    status: 'running',
    progress: 64,
  },
  {
    id: 'logo',
    fileName: 'Logo_Reveal.blend',
    scene: 'Main',
    range: '1-96',
    enabled: false,
    status: 'disabled',
  },
]

function getFrameCount(range: string) {
  const [start, end] = range.split('-').map((part) => Number.parseInt(part.trim(), 10))

  if (!Number.isFinite(start) || !Number.isFinite(end)) return 0

  return Math.max(0, end - start + 1)
}

function getCompletedFrames(item: QueueItem) {
  const frameCount = getFrameCount(item.range)

  if (item.status === 'running' && typeof item.progress === 'number') {
    return Math.round((frameCount * item.progress) / 100)
  }

  return 0
}

function getTaskStatusLabel(item: QueueItem) {
  if (!item.enabled || item.status === 'disabled') return '已停用'
  if (item.status === 'running') return '运行中'

  return '等待中'
}

function rotateValues(values: number[], offset: number) {
  return values.map((_, index) => values[(index + offset) % values.length])
}

function QueueProgressRing({ value }: { value: number }) {
  const radius = 42
  const circumference = 2 * Math.PI * radius
  const normalizedValue = Math.min(100, Math.max(0, value))
  const dashOffset = circumference * (1 - normalizedValue / 100)

  return (
    <div className={styles.queueProgressRing} aria-label={`整体进度 ${normalizedValue}%`}>
      <svg viewBox="0 0 100 100" aria-hidden="true">
        <circle className={styles.progressRingTrack} cx="50" cy="50" r={radius} />
        <circle
          className={styles.progressRingValue}
          cx="50"
          cy="50"
          r={radius}
          style={{
            strokeDasharray: circumference,
            strokeDashoffset: dashOffset,
          }}
        />
      </svg>
      <span>{normalizedValue}%</span>
    </div>
  )
}

function TinyChart({ label, values }: { label: string; values: number[] }) {
  const plotInset = 2
  const plotWidth = 100 - plotInset * 2
  const plotHeight = 44 - plotInset * 2
  const points = values
    .map((value, index) => {
      const x = plotInset + (index / (values.length - 1)) * plotWidth
      const y = plotInset + plotHeight - (value / 100) * plotHeight

      return `${x},${y}`
    })
    .join(' ')

  return (
    <div className={styles.tinyChart}>
      <span className={styles.chartLabel} aria-label={label}>
        {label.split('').map((letter) => (
          <span key={letter} aria-hidden="true">
            {letter}
          </span>
        ))}
      </span>
      <svg viewBox="0 0 100 48" aria-hidden="true">
        <polyline points={`${plotInset},44 ${points} 98,44`} className={styles.chartFill} />
        <polyline points={points} className={styles.chartLine} />
      </svg>
    </div>
  )
}

function QueueRow({
  item,
  selectedId,
  onSelect,
  onToggle,
}: {
  item: QueueItem
  selectedId: string
  onSelect: (id: string) => void
  onToggle: (id: string, event: MouseEvent<HTMLButtonElement>) => void
}) {
  const controls = useDragControls()
  const selected = selectedId === item.id

  const startDrag = (event: PointerEvent<HTMLButtonElement>) => {
    onSelect(item.id)
    controls.start(event)
  }

  return (
    <Reorder.Item
      value={item}
      dragControls={controls}
      dragListener={false}
      className={`${styles.queueRow} ${typeof item.progress === 'number' ? styles.queueRowWithProgress : ''} ${
        selected ? styles.queueRowSelected : ''
      }`}
      onClick={() => onSelect(item.id)}
    >
      <span className={`${styles.statusDot} ${styles[item.status]}`} />
      <span className={styles.taskCopy}>
        <strong>{item.fileName}</strong>
        <small>
          {item.scene} {item.range}
        </small>
      </span>
      {typeof item.progress === 'number' ? (
        <span className={styles.progressRail}>
          <span style={{ width: `${item.progress}%` }} />
        </span>
      ) : null}
      <button
        className={`${styles.switch} ${item.enabled ? styles.switchOn : ''}`}
        type="button"
        aria-label={`${item.enabled ? 'Disable' : 'Enable'} ${item.fileName}`}
        onClick={(event) => onToggle(item.id, event)}
      />
      <button
        className={styles.dragHandle}
        type="button"
        aria-label={`Move ${item.fileName}`}
        onPointerDown={startDrag}
      >
        {Array.from({ length: 6 }).map((_, index) => (
          <span key={index} />
        ))}
      </button>
    </Reorder.Item>
  )
}

function AppMockup() {
  const shouldReduceMotion = useReducedMotion()
  const [items, setItems] = useState(initialItems)
  const [selectedId, setSelectedId] = useState(initialItems[0].id)
  const [hardwareFrame, setHardwareFrame] = useState(0)

  const selectedTask = items.find((item) => item.id === selectedId) ?? items[0]
  const activeTaskCount = items.filter((item) => item.enabled && item.status === 'running').length
  const enabledItems = items.filter((item) => item.enabled)
  const totalFrames = enabledItems.reduce((total, item) => total + getFrameCount(item.range), 0)
  const completedFrames = enabledItems.reduce((total, item) => total + getCompletedFrames(item), 0)
  const overallProgressInt = totalFrames > 0 ? Math.round((completedFrames / totalFrames) * 100) : 0
  const selectedFrameCount = getFrameCount(selectedTask.range)
  const selectedCurrentFrame = Math.max(1, getCompletedFrames(selectedTask))

  useEffect(() => {
    if (shouldReduceMotion) return undefined

    const interval = window.setInterval(() => {
      setHardwareFrame((current) => current + 1)
      setItems((current) =>
        current.map((item) => {
          if (item.status !== 'running' || !item.enabled || typeof item.progress !== 'number') return item

          return {
            ...item,
            progress: item.progress >= 96 ? 18 : item.progress + 2,
          }
        }),
      )
    }, 900)

    return () => window.clearInterval(interval)
  }, [shouldReduceMotion])

  const toggleItem = (id: string, event: MouseEvent<HTMLButtonElement>) => {
    event.stopPropagation()
    setItems((current) =>
      current.map((item) =>
        item.id === id
          ? {
              ...item,
              enabled: !item.enabled,
              status: item.enabled ? 'disabled' : 'waiting',
              progress: item.enabled ? undefined : item.progress,
            }
          : item,
      ),
    )
  }

  return (
    <div className={styles.appWindow} aria-label="Blender Render Queue interface preview">
      <div className={styles.titleBar}>
        <img className={styles.appIcon} src={BRAND_LOGO_SRC} alt="" />
        <strong>Blender Render Queue</strong>
        <div className={styles.windowControls} aria-hidden="true">
          <span />
          <span />
          <span />
        </div>
      </div>

      <div className={styles.appBody}>
        <aside className={styles.sideRail} aria-hidden="true">
          <span className={styles.railButton}>
            <MaterialIcon path={iconPaths.chevronRight} />
          </span>
          <span className={`${styles.railButton} ${styles.railActive}`}>
            <MaterialIcon path={iconPaths.listStatus} />
          </span>
          <span className={styles.railButton}>
            <MaterialIcon path={iconPaths.textBoxSearch} />
          </span>
          <span className={styles.railButton}>
            <MaterialIcon path={iconPaths.settings} />
          </span>
        </aside>

        <section className={styles.workspace}>
          <div className={styles.topGrid}>
            <div className={`${styles.glassPanel} ${styles.queueStatus}`}>
              <div className={styles.queueStatusCopy}>
                <strong>{activeTaskCount > 0 ? `运行中 (${activeTaskCount} 个任务)` : '队列空闲'}</strong>
                <span>成功: 0 | 失败: 0</span>
                <span>
                  帧: {completedFrames} | {totalFrames}
                </span>
              </div>
              {activeTaskCount > 0 ? <QueueProgressRing value={overallProgressInt} /> : null}
            </div>

            <div className={`${styles.glassPanel} ${styles.hardware}`}>
              <TinyChart label="CPU" values={rotateValues(CPU_VALUES, hardwareFrame)} />
              <TinyChart label="GPU" values={rotateValues(GPU_VALUES, hardwareFrame)} />
            </div>

            <div className={`${styles.glassPanel} ${styles.actions}`}>
              <button className={styles.startButton} type="button">
                <MaterialIcon path={iconPaths.blenderSoftware} />
                开始队列
              </button>
              <button className={styles.addButton} type="button" aria-label="Add tasks">
                +
              </button>
              <button className={styles.postRender} type="button">
                <MaterialIcon path={iconPaths.arrowRight} />
                渲染完成后: 无
                <MaterialIcon path={iconPaths.expandDown} />
              </button>
            </div>
          </div>

          <div className={styles.mainGrid}>
            <div className={`${styles.glassPanel} ${styles.taskDetail}`}>
              <div className={styles.detailHeader}>
                <div>
                  <h2>{selectedTask.fileName}</h2>
                  <span>{getTaskStatusLabel(selectedTask)}</span>
                </div>
                <button type="button">
                  打开
                  <MaterialIcon path={iconPaths.expandDown} />
                </button>
              </div>

              <div className={styles.detailCard}>
                <nav className={styles.tabs} aria-label="Task details">
                  <span className={styles.tabActive}>场景信息</span>
                  <span>渲染设置</span>
                  <span>输出预览</span>
                  <span>日志</span>
                </nav>
                <div className={styles.sceneBadge}>⌂ Scene</div>
                <dl className={styles.sceneInfo}>
                  <div>
                    <dt>场景类型:</dt>
                    <dd>单一场景</dd>
                  </div>
                  <div>
                    <dt>帧范围:</dt>
                    <dd>
                      {selectedTask.range}（{selectedFrameCount} 帧）
                    </dd>
                  </div>
                  <div>
                    <dt>当前帧:</dt>
                    <dd>{selectedCurrentFrame}</dd>
                  </div>
                  <div>
                    <dt>帧率:</dt>
                    <dd>24.00 fps</dd>
                  </div>
                  <div>
                    <dt>渲染引擎:</dt>
                    <dd>Eevee</dd>
                  </div>
                  <div>
                    <dt>相机:</dt>
                    <dd>Camera</dd>
                  </div>
                  <div>
                    <dt>输出路径:</dt>
                    <dd>/tmp/</dd>
                  </div>
                  <div>
                    <dt>输出格式:</dt>
                    <dd>PNG</dd>
                  </div>
                </dl>
              </div>

              <div className={styles.metaPanel}>
                <span>大小: 75.4 MB</span>
                <span>创建: 2026-06-04 19:49:08</span>
                <span>路径: /Users/atticus/Desktop/宝石渲染/{selectedTask.fileName}</span>
              </div>
            </div>

            <div className={`${styles.glassPanel} ${styles.queueList}`}>
              <Reorder.Group
                axis="y"
                values={items}
                onReorder={setItems}
                className={styles.queueGroup}
              >
                {items.map((item) => (
                  <QueueRow
                    key={item.id}
                    item={item}
                    selectedId={selectedId}
                    onSelect={setSelectedId}
                    onToggle={toggleItem}
                  />
                ))}
              </Reorder.Group>
            </div>
          </div>

        </section>
      </div>
    </div>
  )
}

function HeroPreview() {
  const { ref: mockupFrameRef, scale: mockupScale } = useMockupScale()
  const hasWasmPreview = usePreviewAssetAvailable(WASM_PREVIEW_SRC)
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const wheelCleanupRef = useRef<(() => void) | null>(null)
  const [isFrameLoaded, setFrameLoaded] = useState(false)
  const mockupScaleStyle = {
    '--mockup-scale': mockupScale,
    '--mockup-height': `${MOCKUP_HEIGHT * mockupScale}px`,
  } as CSSProperties

  useEffect(
    () => () => {
      wheelCleanupRef.current?.()
      wheelCleanupRef.current = null
    },
    [],
  )

  const handleFrameLoad = () => {
    setFrameLoaded(true)
    wheelCleanupRef.current?.()
    wheelCleanupRef.current = null

    const contentWindow = iframeRef.current?.contentWindow
    if (!contentWindow) return

    const forwardWheelToPage = (event: WheelEvent) => {
      event.preventDefault()
      window.scrollBy({
        top: event.deltaY,
        left: event.deltaX,
        behavior: 'auto',
      })
    }

    contentWindow.addEventListener('wheel', forwardWheelToPage, { passive: false })
    wheelCleanupRef.current = () => {
      contentWindow.removeEventListener('wheel', forwardWheelToPage)
    }
  }

  return (
    <div className={styles.previewBlock}>
      <div className={styles.previewPrompt}>
        <span>来试试吧</span>
        <HandDrawnArrow />
      </div>
      <div ref={mockupFrameRef} className={styles.heroVisual} style={mockupScaleStyle}>
        {hasWasmPreview ? (
          <>
            <iframe
              ref={iframeRef}
              className={`${styles.wasmPreviewFrame} ${isFrameLoaded ? styles.frameLoaded : ''}`}
              title="Blender Render Queue live preview"
              src={WASM_PREVIEW_SRC}
              onLoad={handleFrameLoad}
            />
            {!isFrameLoaded ? (
              <div className={styles.previewLoading} role="status" aria-live="polite">
                <span className={styles.loadingSpinner} aria-hidden="true" />
                <span>正在加载预览...</span>
              </div>
            ) : null}
          </>
        ) : (
          <AppMockup />
        )}
      </div>
    </div>
  )
}

function DownloadSection() {
  return (
    <section id="download" className={styles.downloadSection} aria-labelledby="download-title">
      <div className={styles.downloadIntro}>
        <span>下载</span>
        <h2 id="download-title">当前版本 {siteConfig.version}</h2>
        <p>安装包和历史版本统一发布在 GitHub Releases。Windows 为当前主线，macOS 提供可用预览。</p>
        <div className={styles.downloadActions}>
          <a href={siteConfig.releaseUrl} target="_blank" rel="noreferrer">
            GitHub Releases
          </a>
          <a href={siteConfig.githubUrl} target="_blank" rel="noreferrer">
            源码仓库
          </a>
        </div>
      </div>

      <div className={styles.downloadPanel}>
        <div className={styles.platformGrid}>
          {downloadPlatforms.map((platform) => (
            <article key={platform.id} id={platform.href.slice(1)} className={styles.platformCard}>
              <span>{platform.status}</span>
              <h3>{platform.label}</h3>
              {platform.note ? <p>{platform.note}</p> : null}
            </article>
          ))}
        </div>

        <div className={styles.releaseNotes}>
          <span>Release Notes</span>
          <ul>
            {releaseNotes.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  )
}

export function HomePage() {
  const shouldReduceMotion = useReducedMotion()

  return (
    <motion.div
      className={styles.page}
      initial={shouldReduceMotion ? false : { opacity: 0, y: 12 }}
      animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
      transition={{ duration: 0.55, ease: [0.22, 1, 0.36, 1] }}
    >
      <section id="overview" className={styles.hero}>
        <div className={styles.heroCopy}>
          <span className={styles.eyebrow}>BlenderSuite</span>
          <h1>Blender Render Queue</h1>
          <p>
            基于 Avalonia 构建的跨平台高性能 Blender 渲染调度工具。
          </p>
          <div className={styles.heroActions}>
            <a className={styles.primaryCta} href={siteConfig.primaryCta.href}>
              下载 / 获取试用
            </a>
            <a className={styles.secondaryCta} href="#workflow">
              查看工作流
            </a>
          </div>
        </div>

        <HeroPreview />
      </section>

      <section className={styles.capabilityStrip} aria-label="Workflow highlights">
        <span>AOT 原生桌面</span>
        <span>Blender 扩展提交</span>
        <span>序列帧自动合成</span>
      </section>

      <section className={styles.storySection} aria-labelledby="positioning-title">
        <div className={styles.sectionHeader}>
          <span>核心优势</span>
          <h2 id="positioning-title">Blender 渲染任务管理</h2>
          <p>
            适用于连续渲染、批量输出和无人值守任务。桌面端负责队列、监控、输出和完成后动作。
          </p>
        </div>

        <div className={styles.valueGrid}>
          {advantageCards.map((item) => (
            <article key={item.eyebrow} className={styles.valueCard}>
              <span>{item.eyebrow}</span>
              <h3>{item.title}</h3>
              <p>{item.description}</p>
            </article>
          ))}
        </div>
      </section>

      <section id="workflow" className={styles.workflowSection} aria-labelledby="workflow-title">
        <div className={styles.sectionHeader}>
          <span>工作流</span>
          <h2 id="workflow-title">提交、排队、监控、输出</h2>
        </div>

        <ol className={styles.workflowList}>
          {workflowSteps.map((step, index) => (
            <li key={step.title}>
              <span>{String(index + 1).padStart(2, '0')}</span>
              <h3>{step.title}</h3>
              <p>{step.body}</p>
            </li>
          ))}
        </ol>
      </section>

      <section id="features" className={styles.featureSection} aria-labelledby="features-title">
        <div className={styles.sectionHeader}>
          <span>功能</span>
          <h2 id="features-title">核心功能</h2>
        </div>

        <div className={styles.featureGrid}>
          {coreCapabilities.map((feature) => (
            <article key={feature.title}>
              <span>{feature.eyebrow}</span>
              <h3>{feature.title}</h3>
              <p>{feature.description}</p>
            </article>
          ))}
        </div>
      </section>

      <DownloadSection />
    </motion.div>
  )
}
