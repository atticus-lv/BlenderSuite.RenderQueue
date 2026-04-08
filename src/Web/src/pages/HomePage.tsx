import { motion } from 'framer-motion'
import { Link } from 'react-router-dom'
import { siteConfig } from '../content/site'
import styles from './HomePage.module.css'

const containerVariants = {
  hidden: { opacity: 0 },
  visible: { 
    opacity: 1, 
    transition: { staggerChildren: 0.15 } 
  }
}

const itemVariants = {
  hidden: { opacity: 0, y: 20 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.6, ease: [0.22, 1, 0.36, 1] } }
}

export function HomePage() {
  return (
    <motion.div 
      className={styles.page}
      initial="hidden"
      animate="visible"
      variants={containerVariants}
    >
      <motion.header className={styles.hero} variants={itemVariants}>
        <div className={styles.heroContent}>
          <h1 className={styles.heroTitle}>渲染调度系统</h1>
          <p className={styles.summary}>
            基于 Avalonia 构建，运行于 Windows 与 macOS。支持 Blender 场景自动识别，提供任务排队、状态监控与渲染自动化处理。
          </p>
          <div className={styles.heroSpecs}>
            <div className={styles.spec}>
              <span className={styles.specLabel}>架构</span>
              <span className={styles.specValue}>Avalonia UI</span>
            </div>
            <div className={styles.spec}>
              <span className={styles.specLabel}>平台</span>
              <span className={styles.specValue}>Win / macOS</span>
            </div>
            <div className={styles.spec}>
              <span className={styles.specLabel}>集成</span>
              <span className={styles.specValue}>Blender 扩展</span>
            </div>
          </div>
        </div>

        <motion.div 
          className={styles.heroVisual}
          whileHover={{ scale: 1.02 }}
          transition={{ type: "spring", stiffness: 100 }}
        >
          <div className={styles.visualTask}>
            <div className={styles.statusIndicator} />
            <div className={styles.taskInfo}>
              <div style={{ fontWeight: 500 }}>cube</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>Scene 1-1 *</div>
            </div>
          </div>
          <div className={`${styles.visualTask} ${styles.active}`}>
            <div className={styles.statusIndicator} />
            <div className={styles.taskInfo}>
              <div style={{ fontWeight: 500 }}>cube</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>Scene 1-1 *</div>
            </div>
            <motion.div 
              className={styles.progressBarMini}
              initial={{ width: 0 }}
              animate={{ width: '80%' }}
              transition={{ duration: 3, repeat: Infinity, ease: "linear" }}
            />
          </div>
          <div className={styles.visualTask}>
            <div className={`${styles.statusIndicator} ${styles.inactive}`} />
            <div className={styles.taskInfo}>
              <div style={{ fontWeight: 500 }}>cube</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>Scene 1-1 *</div>
            </div>
          </div>
        </motion.div>
      </motion.header>

      <motion.section className={styles.section} variants={itemVariants}>
        <span className={styles.eyebrow}>Core Capabilities</span>
        <div className={styles.grid}>
          {[
            { t: '任务管理', d: '支持渲染任务的优先级排序、暂停与继续。可随时动态覆写渲染参数，无需重复提交。' },
            { t: 'Blender 扩展', d: '原生集成 Blender 交互扩展，支持场景信息一键提交，自动匹配环境配置。' },
            { t: '序列帧管线', d: '内置序列帧预览与自动化合成，支持渲染完成后触发电源管理策略，如自动关机或睡眠。' },
            { t: '运行监控', d: '全链路状态观测，实时反馈任务进度与底层渲染日志，掌握渲染现场每一处变化。' }
          ].map((item, i) => (
            <motion.article 
              key={i} 
              className={styles.item}
              whileHover={{ y: -8, borderColor: 'rgba(20, 150, 255, 0.5)', backgroundColor: 'rgba(20, 150, 255, 0.03)' }}
            >
              <h3>{item.t}</h3>
              <p>{item.d}</p>
            </motion.article>
          ))}
        </div>
      </motion.section>
    </motion.div>
  )
}
