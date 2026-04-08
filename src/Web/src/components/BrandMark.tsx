type BrandMarkProps = {
  compact?: boolean
}

export function BrandMark({ compact = false }: BrandMarkProps) {
  const size = compact ? 34 : 72

  return (
    <img
      src="/branding/logo.png"
      width={size}
      height={size}
      alt=""
      aria-hidden="true"
    />
  )
}
