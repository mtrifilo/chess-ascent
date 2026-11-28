'use client'

import type { Side, Square } from '@/components/chess-board/chess-board'
import { cn } from '@/lib/utils'

import type { PromotionPiece } from './game-state'
import './promotion-picker.css'

const FILES = 'abcdefgh'

// What each option is called to assistive technology.
const PIECE_NAMES: Record<PromotionPiece, string> = {
  queen: 'Queen',
  rook: 'Rook',
  bishop: 'Bishop',
  knight: 'Knight',
}

// The pieces a pawn may become, laid over the board on the square it
// promotes on and running down the file, toward the middle of the board:
// from the top edge downwards, from the bottom edge (Black's promotion with
// White at the bottom, or the board flipped) upwards. It sits inside the
// board's own box, which is positioned, and is sized in eighths of it, one
// square per option. There is no way to cancel: the move is sent once a piece
// is chosen.
//
// The pieces are the board's own artwork: chessground's piece stylesheet
// draws a `piece` element by its role and colour classes inside an element of
// class `cg-wrap`. That class also brings chessground's own position and
// display, so it goes on each option's inner box, never on the picker.
export function PromotionPicker({
  square,
  side,
  orientation,
  pieces,
  onChoose,
}: {
  square: Square
  side: Side
  orientation: Side
  pieces: readonly PromotionPiece[]
  onChoose: (piece: PromotionPiece) => void
}) {
  const file = FILES.indexOf(square[0])
  const column = orientation === 'white' ? file : 7 - file
  const atTop = (square[1] === '8') === (orientation === 'white')

  return (
    <div
      role="group"
      aria-label={`Choose a piece for the pawn on ${square}.`}
      className={cn(
        'promotion-picker absolute z-10 flex w-1/8 rounded-sm border bg-card shadow-popover',
        atTop ? 'top-0 flex-col' : 'bottom-0 flex-col-reverse'
      )}
      style={{ left: `${column * 12.5}%` }}
    >
      {pieces.map(piece => (
        <button
          key={piece}
          type="button"
          aria-label={PIECE_NAMES[piece]}
          className="aspect-square w-full focus-visible:outline-hidden focus-visible:ring-[3px] focus-visible:ring-ring"
          onClick={() => onChoose(piece)}
        >
          <span
            ref={drawPiece(`${piece} ${side}`)}
            className="cg-wrap size-full"
          />
        </button>
      ))}
    </div>
  )
}

// Puts chessground's `piece` element, with the given classes, into the box.
// `piece` is not an HTML element, so it is made here, as chessground makes
// its own, rather than by React, which would warn about an unknown tag.
function drawPiece(classes: string) {
  return (box: HTMLElement | null) => {
    if (!box) return
    const piece = document.createElement('piece')
    piece.className = classes
    box.replaceChildren(piece)
  }
}
