import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { ChessBoard } from './chess-board'

// Renders the real chessground in jsdom. jsdom has no layout, so these tests
// check what is drawn, not where; moving pieces is checked in a browser.
const FEN =
  'r1bqk2r/ppp2ppp/2np1n2/2b1p3/2B1P3/2PP1N2/PP3PPP/RNBQK2R w KQkq - 0 6'

function boardOf(container: HTMLElement) {
  const wrap = container.querySelector('.cg-wrap')
  if (!wrap) throw new Error('chessground did not render')
  return wrap
}

describe('ChessBoard with chessground', () => {
  it('draws every piece of the position', () => {
    const { container } = render(<ChessBoard fen={FEN} />)

    const pieces = boardOf(container).querySelectorAll('cg-board piece')
    expect(pieces).toHaveLength(32)
    expect(
      boardOf(container).querySelectorAll('piece.knight.white')
    ).toHaveLength(2)
  })

  it('labels the left file with ranks and the bottom rank with files', () => {
    const { container } = render(<ChessBoard fen={FEN} />)

    const ranks = boardOf(container).querySelector('coords.ranks')
    const files = boardOf(container).querySelector('coords.files')
    expect(ranks).toHaveClass('left')
    expect(ranks).toHaveTextContent('12345678')
    expect(files).toHaveTextContent('abcdefgh')
  })

  it('marks the last move', () => {
    const { container } = render(
      <ChessBoard fen={FEN} lastMove={{ from: 'd7', to: 'd6' }} />
    )

    expect(
      boardOf(container).querySelectorAll('square.last-move')
    ).toHaveLength(2)
  })

  it('shows Black at the bottom when oriented for Black', () => {
    const { container } = render(<ChessBoard fen={FEN} orientation="black" />)

    expect(boardOf(container)).toHaveClass('orientation-black')
    expect(boardOf(container).querySelector('coords.ranks')).toHaveClass(
      'black'
    )
  })

  it('cannot be moved on when read-only', () => {
    const { container } = render(<ChessBoard fen={FEN} readOnly />)

    expect(boardOf(container)).not.toHaveClass('manipulable')
  })
})
