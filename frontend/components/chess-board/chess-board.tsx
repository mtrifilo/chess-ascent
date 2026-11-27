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
  // Arrows from square to square, in the hint arrow colour.
  arrows?: readonly BoardMove[]
  // Squares filled in the hint colour.
  hintSquares?: readonly Square[]
  // Shows the position only: nothing can be picked up.
  readOnly?: boolean
  // Called with each move the person makes, by drag or by tapping the piece
  // and then the square. The board shows the move at once, and until `fen`
  // changes it lets nothing more move and leaves the move, the last-move and
  // check marks as they are, whatever the other position inputs do. To undo
  // a move without a new `fen`, mount a new board by changing its React
  // `key`.
  onMove?: (move: BoardMove) => void
}

const ARROW_BRUSH = 'arrow'

// The arrow draws in `currentColor`, which the stylesheet sets from the hint
// arrow token. A line width of 11 (in 64ths of a square) matches the design's
// 14px arrow shaft on an 80px square. Chessground merges configured brushes
// into its own defaults, so only this one is given, although its type asks
// for its four default colours too.
const BRUSHES = {
  [ARROW_BRUSH]: {
    key: ARROW_BRUSH,
    color: 'currentColor',
    opacity: 1,
    lineWidth: 11,
  },
} as Partial<DrawBrushes> as DrawBrushes

// The class chessground puts on a hinted square; the stylesheet fills it.
const HINT_CLASS = 'hint'

// The events with which chessground starts a selection or a drag.
const POINTER_START_EVENTS = ['touchstart', 'mousedown'] as const

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
  hintSquares,
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
  const currentFen = useEffectEvent(() => fen)
  const currentMarks = useEffectEvent(() =>
    marksConfig(fen, destinations, lastMove, check, readOnly)
  )
  const currentArrows = useEffectEvent(() => arrowShapes(arrows))
  const currentHints = useEffectEvent(() => hintClasses(hintSquares))
  const currentOrientation = useEffectEvent(() => orientation)

  // Created once, with every input, so it does not animate from a default
  // position. Read-only is applied through `movable` and the effect after
  // this one, rather than chessground's `viewOnly`, which is fixed at
  // creation.
  useEffect(() => {
    const element = elementRef.current
    if (!element) return
    const marks = currentMarks()
    const api = Chessground(element, {
      ...FIXED_CONFIG,
      ...marks,
      fen: currentFen(),
      orientation: currentOrientation(),
      movable: {
        ...FIXED_CONFIG.movable,
        ...marks.movable,
        events: { after: (from, to) => reportMove(from, to) },
      },
      drawable: { ...FIXED_CONFIG.drawable, autoShapes: currentArrows() },
      highlight: { custom: currentHints() },
    })
    apiRef.current = api
    return () => {
      // A piece still held when the board goes away would keep chessground's
      // drag loop running, since destroy() does not end it.
      api.cancelMove()
      api.destroy()
      apiRef.current = null
    }
  }, [])

  // Chessground cancels a touch that starts on or near a piece, to stop the
  // page scrolling under a drag. A read-only board keeps those touches, and
  // clicks, from reaching it, so a swipe across it scrolls the page.
  useEffect(() => {
    const element = elementRef.current
    if (!readOnly || !element) return
    const withhold = (event: Event) => event.stopPropagation()
    for (const type of POINTER_START_EVENTS)
      element.addEventListener(type, withhold, { capture: true })
    return () => {
      for (const type of POINTER_START_EVENTS)
        element.removeEventListener(type, withhold, { capture: true })
    }
  }, [readOnly])

  // Each effect below passes a group of inputs on when its value changes.
  // The keys compare by value, holding only the fields used, so a parent
  // that builds new objects on every render leaves the board alone.

  // The pieces are redrawn from `fen` only when it changes, together with
  // everything that depends on the position.
  useEffect(() => {
    apiRef.current?.set({ fen, ...currentMarks() })
  }, [fen])

  // The rest never resets the pieces, and is held back while a move is
  // shown, so changing it while the move is being sent neither undoes the
  // move nor allows another.
  //
  // A move is shown when chessground's turn differs from the side to move in
  // `fen`: chessground passes the turn the moment it shows the person's
  // move, and only a new `fen` passes it back. Its move callback is not the
  // signal, because chessground calls it on a timer, after the move is
  // already on the board. The inputs then still describe the position
  // before the move, so passing them on would let the same side move again.
  //
  // A Map has no JSON form of its own, so its entries are listed.
  const marksKey = JSON.stringify([
    destinations ? [...destinations] : [],
    lastMove && [lastMove.from, lastMove.to],
    check,
    readOnly,
  ])
  useEffect(() => {
    const api = apiRef.current
    if (!api || api.state.turnColor !== sideToMove(currentFen())) return
    // Drops a piece the person has picked up, so a read-only board shows
    // nothing selected.
    if (readOnly) api.cancelMove()
    api.set(currentMarks())
  }, [marksKey, readOnly])

  useEffect(() => {
    apiRef.current?.set({ orientation })
  }, [orientation])

  const arrowsKey = JSON.stringify(arrows?.map(({ from, to }) => [from, to]))
  useEffect(() => {
    apiRef.current?.setAutoShapes(currentArrows())
  }, [arrowsKey])

  const hintsKey = JSON.stringify(hintSquares)
  useEffect(() => {
    apiRef.current?.set({ highlight: { custom: currentHints() } })
  }, [hintsKey])

  return (
    // Chessground draws nothing that assistive technology can read, so the
    // board is presented as one named image.
    <div
      role="img"
      aria-label="Chess board"
      className={readOnly ? 'chess-board read-only' : 'chess-board'}
    >
      <div ref={elementRef} />
    </div>
  )
}

// Everything chessground shows besides the pieces: whose turn it is, what may
// move where, the last move and check. Read-only gives no side the move and
// no destinations, so nothing can be picked up.
function marksConfig(
  fen: string,
  destinations: ChessBoardProps['destinations'],
  lastMove: BoardMove | undefined,
  check: Square | undefined,
  readOnly: boolean
) {
  const side = sideToMove(fen)
  return {
    turnColor: side,
    lastMove: lastMove ? [lastMove.from, lastMove.to] : undefined,
    check: checkedSide(fen, check),
    movable: {
      color: readOnly ? undefined : side,
      dests: new Map(
        readOnly
          ? []
          : [...(destinations ?? [])].map(([from, to]) => [from, [...to]])
      ),
    },
  } satisfies Config
}

// The second field of a FEN; White when it is missing.
function sideToMove(fen: string): Side {
  return fen.split(' ')[1] === 'b' ? 'black' : 'white'
}

// Chessground marks check by colour, on that colour's king, so the colour is
// read from the king on the square given. A square without a king marks
// nothing rather than some other square.
function checkedSide(fen: string, check: Square | undefined): Side | false {
  const piece = check && readFen(fen).get(check)
  return piece && piece.role === 'king' ? piece.color : false
}

function arrowShapes(arrows: readonly BoardMove[] = []): DrawShape[] {
  return arrows.map(({ from, to }) => ({
    orig: from,
    dest: to,
    brush: ARROW_BRUSH,
  }))
}

function hintClasses(squares: readonly Square[] = []): Map<Key, string> {
  return new Map(squares.map(square => [square, HINT_CLASS]))
}
