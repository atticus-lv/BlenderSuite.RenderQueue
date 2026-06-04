import styles from './SiteFooter.module.css'

export function SiteFooter() {
  return (
    <footer className={styles.footer}>
      <div className={styles.inner}>
        <p className={styles.copyright}>
          Copyright © 2026 BlenderSuite.RenderQueue contributors. Licensed under AGPL-3.0.
        </p>
      </div>
    </footer>
  )
}
