import { motion, useReducedMotion } from 'framer-motion'
import { Link } from 'react-router-dom'
import { navItems, siteConfig } from '../content/site'
import { BrandMark } from './BrandMark'
import styles from './SiteHeader.module.css'

const basePath = import.meta.env.BASE_URL
const anchorHref = (hash: string) => `${basePath}${hash}`.replace(/\/{2,}/g, '/')

export function SiteHeader() {
  const shouldReduceMotion = useReducedMotion()

  return (
    <motion.header
      className={styles.header}
      initial={shouldReduceMotion ? false : { y: -24, opacity: 0 }}
      animate={shouldReduceMotion ? undefined : { y: 0, opacity: 1 }}
      transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className={styles.bar}>
        <Link className={styles.brand} to="/">
          <BrandMark compact />
          <div>
            <span className={styles.brandName}>BlenderSuite</span>
            <span className={styles.productName}>{siteConfig.productName}</span>
          </div>
        </Link>

        <nav className={styles.nav} aria-label="主导航">
          {navItems.map((item) =>
            item.href.startsWith('/') ? (
              <Link key={item.label} className={styles.navLink} to={item.href}>
                {item.label}
              </Link>
            ) : (
              <a
                key={item.label}
                className={styles.navLink}
                href={item.href.startsWith('#') ? anchorHref(item.href) : item.href}
              >
                {item.label}
              </a>
            ),
          )}
        </nav>

      </div>
    </motion.header>
  )
}
