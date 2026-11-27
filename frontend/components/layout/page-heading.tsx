import { cn } from '@/lib/utils'

// A page's top-level heading, in the design's Display/M style (the My games
// screen's heading).
export function PageHeading({
  className,
  children,
}: {
  className?: string
  children: React.ReactNode
}) {
  return (
    <h1 className={cn('font-display text-display-m font-semibold', className)}>
      {children}
    </h1>
  )
}
