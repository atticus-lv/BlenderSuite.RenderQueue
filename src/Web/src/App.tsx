import { BrowserRouter, Navigate, Route, Routes, useLocation, useParams } from 'react-router-dom'
import { useEffect } from 'react'
import { SiteFooter } from './components/SiteFooter'
import { SiteHeader } from './components/SiteHeader'
import { getPreferredLocale, isSupportedLocale } from './content/site'
import { HomePage } from './pages/HomePage'
import styles from './App.module.css'

function ScrollToTop() {
  const { hash, pathname } = useLocation()

  useEffect(() => {
    if (hash) {
      const frame = window.requestAnimationFrame(() => {
        document.getElementById(decodeURIComponent(hash.slice(1)))?.scrollIntoView()
      })

      return () => window.cancelAnimationFrame(frame)
    }

    window.scrollTo({ top: 0, left: 0, behavior: 'instant' as ScrollBehavior })
    return undefined
  }, [hash, pathname])

  return null
}

function useActiveLocale() {
  const { pathname } = useLocation()
  const firstSegment = pathname.split('/').filter(Boolean)[0]

  return isSupportedLocale(firstSegment) ? firstSegment : getPreferredLocale()
}

function LocaleRedirect() {
  const { hash } = useLocation()

  return <Navigate to={`/${getPreferredLocale()}${hash}`} replace />
}

function LocalizedHomeRoute() {
  const { locale } = useParams()
  const { hash } = useLocation()

  if (!isSupportedLocale(locale)) {
    return <Navigate to={`/${getPreferredLocale()}${hash}`} replace />
  }

  return <HomePage locale={locale} />
}

function RoutedApp() {
  const locale = useActiveLocale()

  return (
    <div className={styles.shell}>
      <SiteHeader locale={locale} />
      <main className={styles.main}>
        <Routes>
          <Route path="/" element={<LocaleRedirect />} />
          <Route path="/:locale" element={<LocalizedHomeRoute />} />
          <Route path="*" element={<LocaleRedirect />} />
        </Routes>
      </main>
      <SiteFooter />
    </div>
  )
}

function App() {
  return (
    <BrowserRouter basename={import.meta.env.BASE_URL}>
      <ScrollToTop />
      <RoutedApp />
    </BrowserRouter>
  )
}

export default App
