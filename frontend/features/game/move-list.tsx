'use client'

import { useEffect, useRef } from 'react'

import { cn } from '@/lib/utils'

import { moveRows, type Game } from './game-state'

const cellClass = 'h-7 px-2 font-data text-data-m font-normal'
const headClass =
  'border-b pb-1 pl-2 text-left text-xs font-normal tracking-column-head text-muted-foreground uppercase'

// The moves so far in standard notation, two to a numbered row, with the
// latest marked. Where the list is taller than its box (beside the board from
// `md` up), it scrolls, and keeps the latest move in view. On phones the page
// scrolls instead. The heading row stays in view as the moves scroll, so its
// cells, not the row, carry its rule: a sticky row's own border does not move
// with it.
export function MoveList({
  moves,
  className,
}: {
  moves: Game['moves']
  className?: string
}) {
  const scrollerRef = useRef<HTMLDivElement>(null)
  const rows = moveRows(moves)
  const latest = moves.at(-1)

  // The list is scrolled to its end when a move is added, and again whenever
  // its box changes size, as it does when the alert above it comes or goes.
  useEffect(() => {
    const scroller = scrollerRef.current
    if (!scroller) return
    const toEnd = () => {
      scroller.scrollTop = scroller.scrollHeight
    }
    toEnd()
    const resized = new ResizeObserver(toEnd)
    resized.observe(scroller)
    return () => resized.disconnect()
  }, [moves.length])

  return (
    <div ref={scrollerRef} className={cn('overflow-y-auto', className)}>
      <table className="w-full table-fixed border-separate border-spacing-0">
        <thead className="sticky top-0 bg-background md:bg-card">
          <tr>
            {/* The number column has no heading. */}
            <td className="w-10 border-b" />
            <th scope="col" className={headClass}>
              White
            </th>
            <th scope="col" className={headClass}>
              Black
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td
                colSpan={3}
                className="pt-2 pl-2 text-body-s text-muted-foreground"
              >
                No moves yet.
              </td>
            </tr>
          ) : (
            rows.map(row => (
              <tr key={row.number}>
                <td className={cn(cellClass, 'text-muted-foreground')}>
                  {row.number}.
                </td>
                {[row.white, row.black].map((move, column) => {
                  const isLatest = move === latest
                  return (
                    <td
                      key={column}
                      aria-current={isLatest ? 'step' : undefined}
                      className={cn(
                        cellClass,
                        isLatest && 'bg-accent text-accent-foreground'
                      )}
                    >
                      {move?.san}
                    </td>
                  )
                })}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  )
}
