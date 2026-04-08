import { siteConfig } from '../content/site'
import styles from './SiteFooter.module.css'

export function SiteFooter() {
  return (
    <footer className={styles.footer}>
      <div className={styles.inner}>
        <div>
          <p className={styles.kicker}>BlenderSuite</p>
          <p className={styles.title}>{siteConfig.productName}</p>
        </div>
        <div className={styles.meta}>
          <span>当前版本 {siteConfig.version}</span>
          <a href={siteConfig.githubUrl} target="_blank" rel="noreferrer">
            GitHub
          </a>
          <a href={siteConfig.releaseUrl} target="_blank" rel="noreferrer">
            Releases
          </a>
        </div>
      </div>
    </footer>
  )
}
