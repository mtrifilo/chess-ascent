'use client'

import '@lichess-org/chessground/assets/chessground.base.css'
import '@lichess-org/chessground/assets/chessground.cburnett.css'
import type { Role } from '@lichess-org/chessground/types'
import { useEffect, useRef } from 'react'

import type { Side } from './chess-board'
import './chess-board.css'

// One piece in the board's artwork, filling its container, for use off the
// board, such as in a promotion picker.
//
// Chessground's piece stylesheet draws a `piece` element by its role and
// colour classes inside an element of class `cg-wrap`. `piece` is not an HTML
// element, so it is made here, as chessground makes its own, rather than by
// React, which would warn about an unknown tag.
export function ChessPiece({ role, side }: { role: Role; side: Side }) {
  const boxRef = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    const piece = document.createElement('piece')
    piece.className = `${role} ${side}`
    boxRef.current?.replaceChildren(piece)
  }, [role, side])

  return <span ref={boxRef} aria-hidden className="chess-piece cg-wrap" />
}
