import { motion, useReducedMotion } from 'framer-motion'
import { Link } from 'react-router-dom'
import { WorkflowShowcase } from '../components/WorkflowShowcase'
import {
  featureDepth,
  featureStories,
  siteConfig,
  supportHighlights,
} from '../content/site'
import styles from './HomePage.module.css'

const heroTransition = {
  duration: 0.7,
  ease: [0.22, 1, 0.36, 1] as const,
}

const heroDetails = [
  { label: 'Current Build', value: siteConfig.version },
  { label: 'Primary Platform', value: 'Windows' },
  { label: 'macOS', value: 'Preview Available' },
]

const heroFlow = [
  {
    index: '01',
    title: '从 Blender 送入队列',
    body: '场景、帧段与输出上下文在进入队列前就被整理好。',
  },
  {
    index: '02',
    title: '在桌面端持续观察',
    body: '状态、日志与节奏控制保持可见，不靠猜测盯任务。',
  },
  {
    index: '03',
    title: '把结果接到交付前',
    body: '输出整理与视频合成继续留在同一条主流程里。',
  },
]

export function HomePage() {
  const shouldReduceMotion = useReducedMotion()

  return (
    <div className={styles.page}>
      <section className={styles.hero} id="overview">
        <div className={styles.heroInner}>
          <div className={styles.heroText}>
            <motion.p
              className={styles.kicker}
              initial={shouldReduceMotion ? false : { opacity: 0, y: 18 }}
              animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
              transition={heroTransition}
            >
              BlenderSuite / Render Workflow Tool
            </motion.p>

            <motion.h1
              className={styles.heroTitle}
              initial={shouldReduceMotion ? false : { opacity: 0, y: 24 }}
              animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
              transition={{ ...heroTransition, delay: 0.08 }}
            >
              给 Blender 的队列渲染工作台。
            </motion.h1>

            <motion.p
              className={styles.summary}
              initial={shouldReduceMotion ? false : { opacity: 0, y: 28 }}
              animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
              transition={{ ...heroTransition, delay: 0.14 }}
            >
              {siteConfig.shortDescription}
            </motion.p>

            <motion.div
              className={styles.heroDetails}
              initial={shouldReduceMotion ? false : { opacity: 0, y: 30 }}
              animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
              transition={{ ...heroTransition, delay: 0.18 }}
            >
              {heroDetails.map((detail) => (
                <div key={detail.label} className={styles.heroDetail}>
                  <span>{detail.label}</span>
                  <strong>{detail.value}</strong>
                </div>
              ))}
            </motion.div>

            <motion.div
              className={styles.ctaRow}
              initial={shouldReduceMotion ? false : { opacity: 0, y: 32 }}
              animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
              transition={{ ...heroTransition, delay: 0.24 }}
            >
              <Link className={styles.primaryCta} to={siteConfig.primaryCta.href}>
                {siteConfig.primaryCta.label}
              </Link>
              <a className={styles.secondaryCta} href={siteConfig.secondaryCta.href}>
                {siteConfig.secondaryCta.label}
              </a>
            </motion.div>

            <motion.div
              className={styles.heroProof}
              initial={shouldReduceMotion ? false : { opacity: 0, y: 36 }}
              animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
              transition={{ ...heroTransition, delay: 0.3 }}
            >
              <span>Queue Rendering</span>
              <span>Pause / Resume</span>
              <span>Video Compose</span>
              <span>Blender Extension</span>
            </motion.div>
          </div>

          <div className={styles.heroVisualColumn}>
            <div className={styles.heroVisualTopline}>
              <span>Render workflow poster</span>
              <span>Submit / Queue / Deliver</span>
            </div>

            <div className={styles.heroStage}>
              <div className={styles.heroGlow} />
              <div className={styles.heroWord}>QUEUE</div>
              <div className={styles.heroBeam} />
              <div className={styles.heroSignalGrid}>
                {heroFlow.map((item) => (
                  <article key={item.index} className={styles.heroSignalCard}>
                    <span>{item.index}</span>
                    <strong>{item.title}</strong>
                    <p>{item.body}</p>
                  </article>
                ))}
              </div>

              <div className={styles.heroRail}>
                <span>Queue Control</span>
                <span>Pause / Resume</span>
                <span>Video Compose</span>
              </div>

              <div className={styles.heroCaption}>
                <strong>一块真正用来盯队列的桌面工作台。</strong>
                <span>重点不是把 Blender 打开，而是把提交、运行、观察与收尾接成同一条链路。</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className={styles.support}>
        {supportHighlights.map((item, index) => (
          <article key={item.title} className={styles.supportItem}>
            <span className={styles.supportIndex}>0{index + 1}</span>
            <p className={styles.supportEyebrow}>{item.eyebrow}</p>
            <h2>{item.title}</h2>
            <p>{item.description}</p>
          </article>
        ))}
      </section>

      <section className={styles.intro}>
        <p className={styles.kicker}>What Is Blender Render Queue?</p>
        <div className={styles.introGrid}>
          <h2>不是又一个把 Blender 打开的启动器，而是一条更稳定的渲染流程入口。</h2>
          <div className={styles.introCopy}>
            <p className={styles.introBody}>
              Blender Render Queue 的核心不是抽象的“效率提升”，而是把提交、排队、观察、暂停、恢复、输出这些动作收进一套连续工作流。你不必再靠记忆维持上下文，也不必在多个窗口之间来回确认当前任务跑到了哪里。
            </p>
            <p className={styles.introAside}>
              它更接近一块常驻桌面、随时可看的工作台，而不是偶尔打开一次的辅助脚本。
            </p>
          </div>
        </div>
      </section>

      <WorkflowShowcase />

      <section className={styles.storySections} id="stories">
        {featureStories.map((story, index) => (
          <article
            key={story.id}
            className={index % 2 === 0 ? styles.story : styles.storyReverse}
          >
            <div className={styles.storyCopy}>
              <p className={styles.storyEyebrow}>{story.eyebrow}</p>
              <h2>{story.title}</h2>
              <p className={styles.storyIntro}>{story.intro}</p>
              <p className={styles.storyBody}>{story.body}</p>
              <div className={styles.storyPoints}>
                {story.points.map((point) => (
                  <p key={point}>{point}</p>
                ))}
              </div>
            </div>

            <div className={styles.storyVisual}>
              <div className={styles.storyVisualInner}>
                <span className={styles.storyAccent}>{story.accentLabel}</span>
                <div className={styles.storyVisualFrame}>
                  {story.id === 'queue' && (
                    <>
                      <div className={styles.storyQueueHeader}>
                        <span>任务 03</span>
                        <span>Running</span>
                      </div>
                      <div className={styles.storyQueueList}>
                        <div className={styles.storyQueueItemActive}>
                          <strong>Forest pass</strong>
                          <span>暂停 / 恢复 / 停止</span>
                        </div>
                        <div className={styles.storyQueueItem}>
                          <strong>Logo reveal</strong>
                          <span>Queued</span>
                        </div>
                        <div className={styles.storyQueueActions}>
                          <span>Pause</span>
                          <span>Resume</span>
                          <span>Stop</span>
                        </div>
                      </div>
                    </>
                  )}

                  {story.id === 'performance' && (
                    <>
                      <div className={styles.storyPerformanceHero}>
                        <strong>Native</strong>
                        <span>Fast launch · Calm runtime</span>
                      </div>
                      <div className={styles.storyPerformanceStats}>
                        <div>
                          <strong>即时</strong>
                          <span>启动感受</span>
                        </div>
                        <div>
                          <strong>专注</strong>
                          <span>原生工作台</span>
                        </div>
                      </div>
                      <div className={styles.storyPerformanceBars}>
                        <span />
                        <span />
                        <span />
                      </div>
                    </>
                  )}

                  {story.id === 'extension' && (
                    <>
                      <div className={styles.storyExtensionTop}>
                        <div className={styles.storyExtensionNode}>
                          <strong>Blender</strong>
                          <span>Extension</span>
                        </div>
                        <div className={styles.storyExtensionLine} />
                        <div className={styles.storyExtensionNodeAccent}>
                          <strong>Queue</strong>
                          <span>Desktop App</span>
                        </div>
                      </div>
                      <div className={styles.storyExtensionCard}>
                        <span>本地提交</span>
                        <strong>场景信息、输出设置、队列入口一次衔接</strong>
                      </div>
                    </>
                  )}
                </div>
              </div>
            </div>
          </article>
        ))}
      </section>

      <section className={styles.features} id="features">
        <div className={styles.featuresHeader}>
          <p className={styles.kicker}>Feature Depth</p>
          <h2>第一版官网不讲空话，直接把现有能力讲清楚。</h2>
          <p className={styles.featuresLead}>
            从队列控制到场景覆写，再到最后的视频整理，功能描述尽量贴近真实使用动作，而不是抽象名词堆叠。
          </p>
        </div>

        <div className={styles.featureGrid}>
          {featureDepth.map((feature) => (
            <article key={feature.title} className={styles.featureItem}>
              <p className={styles.featureEyebrow}>{feature.eyebrow}</p>
              <h3>{feature.title}</h3>
              <p>{feature.description}</p>
            </article>
          ))}
        </div>
      </section>

      <section className={styles.finalCta}>
        <p className={styles.kicker}>Ready to Try</p>
        <h2>先把下载入口、平台状态和试用承接做完整。</h2>
        <p className={styles.finalCopy}>
          第一版官网先把产品气质、能力边界与下载路径说明白。后续无论接授权站、发码还是正式售卖，入口都可以继续往这套结构上叠加。
        </p>
        <div className={styles.finalActions}>
          <Link className={styles.primaryCta} to={siteConfig.primaryCta.href}>
            打开下载页
          </Link>
          <a
            className={styles.secondaryCta}
            href={siteConfig.githubUrl}
            target="_blank"
            rel="noreferrer"
          >
            查看 GitHub
          </a>
        </div>
      </section>
    </div>
  )
}
