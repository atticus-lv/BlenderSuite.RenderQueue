import {
  motion,
  useMotionValueEvent,
  useReducedMotion,
  useScroll,
} from 'framer-motion'
import { startTransition, useRef, useState } from 'react'
import { siteContent } from '../content/site'
import styles from './WorkflowShowcase.module.css'

const workflowSteps = siteContent.zh.workflowSteps

const panelNotes = [
  '收拢多个 Blender 场景与参数设置。',
  '在长任务过程中保持状态透明。',
  '把输出整理与交付动作接回主流程。',
]

export function WorkflowShowcase() {
  const shouldReduceMotion = useReducedMotion()
  const sectionRef = useRef<HTMLElement | null>(null)
  const [activeIndex, setActiveIndex] = useState(0)
  const { scrollYProgress } = useScroll({
    target: sectionRef,
    offset: ['start start', 'end end'],
  })

  useMotionValueEvent(scrollYProgress, 'change', (value) => {
    if (shouldReduceMotion) {
      return
    }

    const nextIndex = Math.min(
      workflowSteps.length - 1,
      Math.floor(value * workflowSteps.length),
    )

    startTransition(() => {
      setActiveIndex(nextIndex)
    })
  })

  return (
    <section ref={sectionRef} className={styles.section} id="workflow">
      <div className={styles.sticky}>
        <div className={styles.copy}>
          <p className={styles.eyebrow}>Workflow</p>
          <h2>从 Blender 到最终输出，不再靠临时记忆维持流程。</h2>
          <p className={styles.lead}>
            官网中段用一段固定视图，模拟这个工具最核心的价值：让渲染成为一条可以观察、暂停、继续和收尾的链路。
          </p>
        </div>

        <div className={styles.grid}>
          <div className={styles.steps}>
            {workflowSteps.map((step, index) => {
              const isActive = index === activeIndex || shouldReduceMotion

              return (
                <motion.article
                  key={step.title}
                  className={isActive ? styles.stepActive : styles.step}
                  animate={
                    shouldReduceMotion ? undefined : { opacity: isActive ? 1 : 0.45 }
                  }
                  transition={{ duration: 0.25, ease: 'easeOut' }}
                >
                  <span className={styles.stepNumber}>0{index + 1}</span>
                  <div>
                    <h3>{step.title}</h3>
                    <p>{step.body}</p>
                  </div>
                </motion.article>
              )
            })}
          </div>

          <motion.div
            className={styles.panel}
            animate={shouldReduceMotion ? undefined : { y: [0, -6, 0] }}
            transition={{
              duration: 5.2,
              repeat: Number.POSITIVE_INFINITY,
              ease: 'easeInOut',
            }}
          >
            <div className={styles.panelHeader}>
              <span className={styles.panelLabel}>Step {activeIndex + 1}</span>
              <strong>{workflowSteps[activeIndex].title}</strong>
            </div>
            <div className={styles.panelBody}>
              <div className={styles.panelOrbit}>
                <div className={styles.panelCore}>
                  <span>Queue Flow</span>
                  <strong>{activeIndex + 1}/3</strong>
                </div>
              </div>
              <div className={styles.panelMeta}>
                <p>{panelNotes[activeIndex]}</p>
                <div className={styles.progressTrack}>
                  {workflowSteps.map((step, index) => (
                    <span
                      key={step.title}
                      className={
                        index <= activeIndex ? styles.progressActive : styles.progress
                      }
                    />
                  ))}
                </div>
              </div>
            </div>
          </motion.div>
        </div>
      </div>
    </section>
  )
}
