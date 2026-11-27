import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { ChessBoard, type ChessBoardProps } from './chess-board'

// Renders the real chessground in jsdom. jsdom has no layout, so these tests
// check what is drawn, not where; moving pieces is checked in a browser.
const FEN =
  'r1bqk2r/ppp2ppp/2np1n2/2b1p3/2B1P3/2PP1N2/PP3PPP/RNBQK2R w KQkq - 0 6'

// Renders the board and returns chessground's root element.
function renderBoard(props: Partial<ChessBoardProps> = {}) {
  const { container } = render(<ChessBoard fen={FEN} {...props} />)
  const wrap = container.querySelector('.cg-wrap')
  if (!wrap) throw new Error('chessground did not render')
  return wrap
}

describe('ChessBoard with chessground', () => {
  it('draws every piece of the position', () => {
    const board = renderBoard()

    expect(board.querySelectorAll('cg-board piece')).toHaveLength(32)
    expect(board.querySelectorAll('piece.knight.white')).toHaveLength(2)
  })

  it('labels the left file with ranks and the bottom rank with files', () => {
    const board = renderBoard()

    const ranks = board.querySelector('coords.ranks')
    expect(ranks).toHaveClass('left')
    expect(ranks).toHaveTextContent('12345678')
    expect(board.querySelector('coords.files')).toHaveTextContent('abcdefgh')
  })

  it('marks the last move', () => {
    const board = renderBoard({ lastMove: { from: 'd7', to: 'd6' } })

    expect(board.querySelectorAll('square.last-move')).toHaveLength(2)
  })

  it('shows Black at the bottom when oriented for Black', () => {
    const board = renderBoard({ orientation: 'black' })

    expect(board).toHaveClass('orientation-black')
    expect(board.querySelector('coords.ranks')).toHaveClass('black')
  })

  it('cannot be moved on when read-only', () => {
    expect(renderBoard({ readOnly: true })).not.toHaveClass('manipulable')
  })
})
