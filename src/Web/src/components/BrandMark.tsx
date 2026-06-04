type BrandMarkProps = {
  compact?: boolean
}

const logoSrc = `${import.meta.env.BASE_URL}branding/logo.png`

export function BrandMark({ compact = false }: BrandMarkProps) {
  const size = compact ? 34 : 72

  return (
    <img
      src={logoSrc}
      width={size}
      height={size}
      alt=""
      aria-hidden="true"
    />
  )
}
