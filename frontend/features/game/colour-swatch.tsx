import type { Side } from '@/components/chess-board/chess-board'
import { cn } from '@/lib/utils'

// A small square in a side's piece colour, beside the text that names the
// side, so it is hidden from screen readers.
export function ColourSwatch({ side }: { side: Side }) {
  return (
    <span
      aria-hidden
      className={cn(
        'size-3.5 shrink-0 rounded-sm border border-separator',
        side === 'white' ? 'bg-piece-white' : 'bg-piece-black'
      )}
    />
  )
}
