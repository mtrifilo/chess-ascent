'use client'

import { Chessground } from '@lichess-org/chessground'
import type { Api } from '@lichess-org/chessground/api'
import '@lichess-org/chessground/assets/chessground.base.css'
import '@lichess-org/chessground/assets/chessground.cburnett.css'
import type { Config } from '@lichess-org/chessground/config'
import type { DrawBrushes, DrawShape } from '@lichess-org/chessground/draw'
import { read as readFen } from '@lichess-org/chessground/fen'
import type { Color, Key } from '@lichess-org/chessground/types'
import { useEffect, useEffectEvent, useRef } from 'react'

import './chess-board.css'

// A square by its name, such as "e4". Chessground's Key also covers "a0", its
// off-board square for dropped pieces, which this board never enables.
export type Square = Exclude<Key, 'a0'>

export type Side = Color

export type BoardMove = { from: Square; to: Square }

export type ChessBoardProps = {
  // The position in Forsyth-Edwards Notation. Its second field, the side to
  // move, decides whose pieces can be picked up; without it, White's.
  fen: string
  // The side shown at the bottom.
  orientation?: Side
  // For each square whose piece may move, the squares it may move to. The
  // board shows them when the piece is picked up and refuses any other move.
  destinations?: ReadonlyMap<Square, readonly Square[]>
  lastMove?: BoardMove
  // The square of the king in check.
  check?: Square
  arrows?: readonly BoardMove[]
  circles?: readonly Square[]
  // Shows the position only: nothing can be picked up.
  readOnly?: boolean
  // Called with each move the person makes, by drag or by tapping the piece
  // and then the square. The board shows the move at once and keeps showing
  // it until its inputs change.
  onMove?: (move: BoardMove) => void
}

const ARROW_BRUSH = 'arrow'
const CIRCLE_BRUSH = 'circle'

// A brush draws in `currentColor`, which the stylesheet sets from the hint
// tokens. A line width of 11 (in 64ths of a square) matches the design's 14px
// arrow shaft on an 80px square.
const brush = (key: string) => ({
  key,
  color: 'currentColor',
  opacity: 1,
  lineWidth: 11,
})

// Chessground merges configured brushes into its own defaults, so only these
// two are given, although its type asks for its four default colours too.
const BRUSHES = {
  [ARROW_BRUSH]: brush(ARROW_BRUSH),
  [CIRCLE_BRUSH]: brush(CIRCLE_BRUSH),
} as Partial<DrawBrushes> as DrawBrushes

// The parts of chessground's configuration that never change: legality comes
// only from `destinations` (`free: false`), and the person cannot premove or
// draw their own shapes.
const FIXED_CONFIG = {
  coordinates: true,
  ranksPosition: 'left',
  movable: { free: false, showDests: true },
  premovable: { enabled: false },
  drawable: { enabled: false, brushes: BRUSHES },
} satisfies Config

// A React wrapper around chessground, Lichess's board. Chessground is not a
// React library: it owns the DOM inside the element it is given and is driven
// through calls. This component creates it once, passes each input on when
// that input's value changes, and destroys it on unmount. React never renders
// anything inside that element.
export function ChessBoard({
  fen,
  orientation = 'white',
  destinations,
  lastMove,
  check,
  arrows,
  circles,
  readOnly = false,
  onMove,
}: ChessBoardProps) {
  const elementRef = useRef<HTMLDivElement>(null)
  const apiRef = useRef<Api | null>(null)

  // Effect events read the latest props without being effect dependencies,
  // so a new onMove function, or a re-render with equal inputs, leaves the
  // board alone.
  const reportMove = useEffectEvent((from: Key, to: Key) => {
    onMove?.({ from: from as Square, to: to as Square })
  })
  const currentPosition = useEffectEvent(() =>
    positionConfig(fen, destinations, lastMove, check)
  )
  const currentShapes = useEffectEvent(() => toShapes(arrows, circles))
  const currentOrientation = useEffectEvent(() => orientation)

  // Created with every input at once, so it does not animate from a default
  // position. Read-only is fixed at creation, because chessground binds its
  // pointer listeners then, so a change to it creates the board again.
  useEffect(() => {
    const element = elementRef.current
    if (!element) return
    const position = currentPosition()
    const api = Chessground(element, {
      ...FIXED_CONFIG,
      ...position,
      orientation: currentOrientation(),
      viewOnly: readOnly,
      movable: {
        ...FIXED_CONFIG.movable,
        ...position.movable,
        events: { after: (from, to) => reportMove(from, to) },
      },
      drawable: { ...FIXED_CONFIG.drawable, autoShapes: currentShapes() },
    })
    apiRef.current = api
    return () => {
      api.destroy()
      apiRef.current = null
    }
  }, [readOnly])

  // Each effect below passes one group of inputs on when its value changes.
  // The keys compare by value, so a parent that builds a new Map or array on
  // every render does not undo a move the person has just made.
  const positionKey = JSON.stringify([
    fen,
    destinations ? [...destinations] : [],
    lastMove,
    check,
  ])
  useEffect(() => {
    apiRef.current?.set(currentPosition())
  }, [positionKey])

  useEffect(() => {
    apiRef.current?.set({ orientation })
  }, [orientation])

  const shapesKey = JSON.stringify([arrows, circles])
  useEffect(() => {
    apiRef.current?.setAutoShapes(currentShapes())
  }, [shapesKey])

  return (
    // Chessground draws nothing that assistive technology can read, so the
    // board is presented as one named image.
    <div role="img" aria-label="Chess board" className="chess-board">
      <div ref={elementRef} />
    </div>
  )
}

function positionConfig(
  fen: string,
  destinations: ChessBoardProps['destinations'],
  lastMove: BoardMove | undefined,
  check: Square | undefined
) {
  const side: Side = fen.split(' ')[1] === 'b' ? 'black' : 'white'
  return {
    fen,
    turnColor: side,
    lastMove: lastMove ? [lastMove.from, lastMove.to] : undefined,
    check: checkedSide(fen, check),
    movable: {
      color: side,
      dests: new Map(
        [...(destinations ?? [])].map(([from, to]) => [from, [...to]])
      ),
    },
  } satisfies Config
}

// Chessground marks check by colour, on that colour's king, so the colour is
// read from the king on the square given. A square without a king marks
// nothing rather than some other square.
function checkedSide(fen: string, check: Square | undefined): Side | false {
  const piece = check && readFen(fen).get(check)
  return piece && piece.role === 'king' ? piece.color : false
}

function toShapes(
  arrows: readonly BoardMove[] = [],
  circles: readonly Square[] = []
): DrawShape[] {
  return [
    ...arrows.map(({ from, to }) => ({
      orig: from,
      dest: to,
      brush: ARROW_BRUSH,
    })),
    ...circles.map(square => ({ orig: square, brush: CIRCLE_BRUSH })),
  ]
}
