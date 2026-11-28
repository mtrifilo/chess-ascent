import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

// The marker of a game in which a hint was used, on the game screen and in
// My games: the design's outline badge, 22px tall, with the small corners the
// design gives badges.
export function HintUsed({
  className,
  children,
}: {
  className?: string
  children: React.ReactNode
}) {
  return (
    <Badge
      variant="outline"
      className={cn('rounded-sm px-2 py-1 leading-3.5', className)}
    >
      {children}
    </Badge>
  )
}
