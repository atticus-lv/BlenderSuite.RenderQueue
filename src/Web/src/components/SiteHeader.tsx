import { motion, useReducedMotion } from 'framer-motion'
import { Link, useLocation } from 'react-router-dom'
import type { SiteLocale } from '../content/site'
import { siteContent } from '../content/site'
import { BrandMark } from './BrandMark'
import styles from './SiteHeader.module.css'

export function SiteHeader({ locale }: { locale: SiteLocale }) {
  const shouldReduceMotion = useReducedMotion()
  const { hash } = useLocation()
  const content = siteContent[locale]

  return (
    <motion.header
      className={styles.header}
      initial={shouldReduceMotion ? false : { y: -24, opacity: 0 }}
      animate={shouldReduceMotion ? undefined : { y: 0, opacity: 1 }}
      transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className={styles.bar}>
        <Link className={styles.brand} to={`/${locale}`}>
          <BrandMark compact />
          <div>
            <span className={styles.brandName}>Blender Suite</span>
            <span className={styles.productName}>Render Queue</span>
          </div>
        </Link>

        <div className={styles.navCluster}>
          <nav className={styles.nav} aria-label={locale === 'zh' ? '主导航' : 'Main navigation'}>
            {content.navItems.map((item) => (
              <Link key={item.href} className={styles.navLink} to={`/${locale}${item.href}`}>
                {item.label}
              </Link>
            ))}
          </nav>

          <div className={styles.languageSwitch} aria-label={content.languageToggleLabel}>
            <Link
              className={`${styles.languageOption} ${locale === 'zh' ? styles.languageOptionActive : ''}`}
              to={`/zh${hash}`}
            >
              中文
            </Link>
            <Link
              className={`${styles.languageOption} ${locale === 'en' ? styles.languageOptionActive : ''}`}
              to={`/en${hash}`}
            >
              EN
            </Link>
          </div>
        </div>
      </div>
    </motion.header>
  )
}
