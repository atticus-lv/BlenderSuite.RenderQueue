import { motion, useReducedMotion } from 'framer-motion'
import { downloadPlatforms, releaseNotes, siteConfig } from '../content/site'
import styles from './DownloadPage.module.css'

export function DownloadPage() {
  const shouldReduceMotion = useReducedMotion()

  return (
    <div className={styles.page}>
      <section className={styles.hero}>
        <p className={styles.kicker}>Download</p>
        <h1>先把下载入口、平台状态和版本信息摆清楚。</h1>
        <p className={styles.summary}>
          这里先承接官网的主 CTA。第一版用静态结构把平台状态讲透，后续再把真实安装包、授权流程和发码逻辑接进来。
        </p>
      </section>

      <section className={styles.platforms}>
        {downloadPlatforms.map((platform, index) => (
          <motion.article
            key={platform.id}
            className={styles.platformCard}
            initial={shouldReduceMotion ? false : { opacity: 0, y: 22 }}
            animate={shouldReduceMotion ? undefined : { opacity: 1, y: 0 }}
            transition={{
              duration: 0.45,
              ease: [0.22, 1, 0.36, 1],
              delay: index * 0.08,
            }}
            whileHover={shouldReduceMotion ? undefined : { y: -6 }}
          >
            <div className={styles.platformHead}>
              <div>
                <p className={styles.platformStatus}>{platform.status}</p>
                <h2>{platform.label}</h2>
              </div>
              <span className={styles.platformBadge}>BRQ</span>
            </div>
            <p className={styles.platformNote}>{platform.note}</p>
            <a className={styles.platformAction} href={platform.href}>
              {platform.status === '实验归档' ? '查看归档方向' : '准备下载入口'}
            </a>
          </motion.article>
        ))}
      </section>

      <section className={styles.details}>
        <article className={styles.releaseCard}>
          <p className={styles.kicker}>Release Notes</p>
          <h2>当前版本 {siteConfig.version}</h2>
          <ul>
            {releaseNotes.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </article>

        <article className={styles.linksCard}>
          <p className={styles.kicker}>Secondary Links</p>
          <h2>先用这些入口承接更多信息。</h2>
          <div className={styles.linkList}>
            <a href={siteConfig.releaseUrl} target="_blank" rel="noreferrer">
              GitHub Releases
            </a>
            <a href={siteConfig.githubUrl} target="_blank" rel="noreferrer">
              源码仓库
            </a>
            <a href="/">返回首页</a>
          </div>
        </article>
      </section>
    </div>
  )
}
