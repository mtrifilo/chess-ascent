'use client'

import { key2pos } from '@lichess-org/chessground/util'

import type { Side, Square } from '@/components/chess-board/chess-board'
import { ChessPiece } from '@/components/chess-board/chess-piece'
import { cn } from '@/lib/utils'

import type { PromotionPiece } from './game-state'

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
  const [file, rank] = key2pos(square)
  const column = orientation === 'white' ? file : 7 - file
  const atTop = (rank === 7) === (orientation === 'white')

  return (
    <div
      role="group"
      aria-label={`Choose a piece for the pawn on ${square}.`}
      className={cn(
        'absolute z-10 flex w-1/8 rounded-sm border bg-card shadow-popover',
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
          <ChessPiece role={piece} side={side} />
        </button>
      ))}
    </div>
  )
}
