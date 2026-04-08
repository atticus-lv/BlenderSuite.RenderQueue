import { motion, useReducedMotion } from 'framer-motion'
import styles from './RenderQueueMock.module.css'

const queueLanes = [
  { id: 'lane-a', label: 'Queue A', width: '82%' },
  { id: 'lane-b', label: 'Queue B', width: '64%' },
  { id: 'lane-c', label: 'Queue C', width: '91%' },
]

export function RenderQueueMock() {
  const shouldReduceMotion = useReducedMotion()

  return (
    <motion.div
      className={styles.canvas}
      initial={shouldReduceMotion ? false : { opacity: 0, y: 28, scale: 0.985 }}
      animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.72, ease: [0.22, 1, 0.36, 1], delay: 0.12 }}
    >
      <div className={styles.glowA} />
      <div className={styles.glowB} />

      <div className={styles.grid} />

      <div className={styles.topline}>
        <span className={styles.toplineTag}>Queue-driven rendering</span>
        <span className={styles.toplineMeta}>Native desktop workflow</span>
      </div>

      <div className={styles.core}>
        <div className={styles.ringCluster}>
          <div className={styles.outerRing}>
            <div className={styles.middleRing}>
              <div className={styles.innerCore}>
                <span className={styles.coreLabel}>Active frame</span>
                <strong>214</strong>
              </div>
            </div>
          </div>
          <div className={styles.coreLegend}>
            <span>Pause</span>
            <span>Resume</span>
            <span>Compose</span>
          </div>
        </div>

        <div className={styles.rightColumn}>
          <div className={styles.metricBlock}>
            <span>Queue throughput</span>
            <strong>03 tasks</strong>
          </div>

          <div className={styles.queuePanel}>
            {queueLanes.map((lane, index) => (
              <div key={lane.id} className={styles.queueRow}>
                <div className={styles.rowHead}>
                  <span className={styles.rowIndex}>0{index + 1}</span>
                  <span className={styles.rowLabel}>{lane.label}</span>
                </div>
                <div className={styles.rowTrack}>
                  <div className={styles.rowFill} style={{ width: lane.width }} />
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      <div className={styles.bottomBand}>
        <div className={styles.bandPillPrimary}>Render Queue</div>
        <div className={styles.bandPill}>Frame Overrides</div>
        <div className={styles.bandPill}>Video Compose</div>
        <div className={styles.bandPillGhost}>Blender Extension Flow</div>
      </div>
    </motion.div>
  )
}
