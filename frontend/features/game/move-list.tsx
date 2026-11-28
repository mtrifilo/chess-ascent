'use client'

import { useEffect, useRef } from 'react'

import { cn } from '@/lib/utils'

import { moveRows, type Game } from './game-state'

type Move = Game['moves'][number]

const cellClass = 'h-7 px-2 font-data text-data-m font-normal'
const headClass =
  'border-b pb-1 pl-2 text-left text-xs font-normal tracking-column-head text-muted-foreground uppercase'

// The moves in standard notation, two to a numbered row, with one marked:
// the latest, or `current` when given. Given `onSelect`, each move is a
// button that reports it, 44px tall on phones for touch.
//
// Where the list is taller than its box (beside the board from `md` up), it
// scrolls, and keeps the marked move in view. On phones the page scrolls
// instead. The heading row stays in view as the moves scroll, so its cells,
// not the row, carry its rule: a sticky row's own border does not move with
// it.
export function MoveList({
  moves,
  current = moves.at(-1),
  onSelect,
  className,
}: {
  moves: Game['moves']
  current?: Move
  onSelect?: (move: Move) => void
  className?: string
}) {
  const scrollerRef = useRef<HTMLDivElement>(null)
  const rows = moveRows(moves)
  // By place rather than by object, so a fresh read of the same moves
  // scrolls nothing.
  const markedIndex = current ? moves.indexOf(current) : -1
  const markedIsLatest = markedIndex === moves.length - 1

  // The list is scrolled to the marked move when a move is added or another
  // is marked, and again whenever its box changes size, as it does when the
  // alert above it comes or goes. The latest move is shown by scrolling to
  // the end.
  useEffect(() => {
    const scroller = scrollerRef.current
    if (!scroller) return
    const reveal = () => {
      if (markedIsLatest) scroller.scrollTop = scroller.scrollHeight
      else keepMarkedInView(scroller)
    }
    reveal()
    const resized = new ResizeObserver(reveal)
    resized.observe(scroller)
    return () => resized.disconnect()
  }, [moves.length, markedIndex, markedIsLatest])

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
                  const isMarked = move !== undefined && move === current
                  const marked = isMarked ? 'step' : undefined
                  return (
                    <td
                      key={column}
                      data-marked={isMarked || undefined}
                      aria-current={onSelect ? undefined : marked}
                      className={cn(
                        cellClass,
                        onSelect && move && 'p-0',
                        isMarked && 'bg-accent text-accent-foreground'
                      )}
                    >
                      {onSelect && move ? (
                        <button
                          type="button"
                          aria-current={marked}
                          className="h-11 w-full px-2 text-left outline-none focus-visible:ring-[3px] focus-visible:ring-ring focus-visible:ring-inset md:h-7"
                          onClick={() => onSelect(move)}
                        >
                          {move.san}
                        </button>
                      ) : (
                        move?.san
                      )}
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

// Scrolls the list by as little as shows the marked move whole, below the
// heading row that stays at the top. A list that does not scroll, as on
// phones, is left alone.
function keepMarkedInView(scroller: HTMLElement) {
  const cell = scroller.querySelector('[data-marked]')
  const head = scroller.querySelector('thead')
  if (!cell || !head) return
  const box = scroller.getBoundingClientRect()
  const top = box.top + head.getBoundingClientRect().height
  const { top: cellTop, bottom: cellBottom } = cell.getBoundingClientRect()
  if (cellTop < top) scroller.scrollTop -= top - cellTop
  else if (cellBottom > box.bottom)
    scroller.scrollTop += cellBottom - box.bottom
}
