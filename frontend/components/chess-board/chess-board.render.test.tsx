import type { Api } from '@lichess-org/chessground/api'
import { render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ChessBoard, type ChessBoardProps } from './chess-board'

// Renders the real chessground in jsdom. jsdom has no layout, so these tests
// check what is drawn and which moves are accepted, not where pieces sit.
// Chessground ignores untrusted pointer events, so taps go through its API
// (`selectSquare`, what a tap calls); dragging is checked in a browser.

// The real chessground, with the board it creates kept for the test to tap.
const created = vi.hoisted(() => ({ api: undefined as Api | undefined }))
vi.mock('@lichess-org/chessground', async importOriginal => {
  const real = await importOriginal<typeof import('@lichess-org/chessground')>()
  return {
    Chessground: (...args: Parameters<typeof real.Chessground>) =>
      (created.api = real.Chessground(...args)),
  }
})

const FEN =
  'r1bqk2r/ppp2ppp/2np1n2/2b1p3/2B1P3/2PP1N2/PP3PPP/RNBQK2R w KQkq - 0 6'
const PLACEMENT = FEN.split(' ')[0]
const KNIGHT_MOVES = new Map([
  ['f3', ['e5', 'g5', 'd4', 'h4', 'd2', 'g1']],
] as const)

// Renders the board and returns chessground's root element.
function renderBoard(props: Partial<ChessBoardProps> = {}) {
  const { container } = render(<ChessBoard fen={FEN} {...props} />)
  const wrap = container.querySelector('.cg-wrap')
  if (!wrap) throw new Error('chessground did not render')
  return wrap
}

function tap(...squares: Parameters<Api['selectSquare']>[0][]) {
  for (const square of squares) created.api!.selectSquare(square)
}

// Chessground calls its move callback on a timer.
const settle = () => new Promise(resolve => setTimeout(resolve, 10))

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

  it('reports a move made by tapping the piece, then a destination', async () => {
    const onMove = vi.fn()
    renderBoard({ destinations: KNIGHT_MOVES, onMove })

    tap('f3', 'g5')
    await settle()

    expect(onMove).toHaveBeenCalledWith({ from: 'f3', to: 'g5' })
    expect(created.api!.getFen()).not.toBe(PLACEMENT)
  })

  it('refuses a square that is not a destination, leaving the piece', async () => {
    const onMove = vi.fn()
    renderBoard({ destinations: KNIGHT_MOVES, onMove })

    tap('f3', 'f5')
    await settle()

    expect(onMove).not.toHaveBeenCalled()
    expect(created.api!.getFen()).toBe(PLACEMENT)
  })

  it('lets nothing move when read-only', async () => {
    const onMove = vi.fn()
    renderBoard({ destinations: KNIGHT_MOVES, onMove, readOnly: true })

    tap('f3', 'g5')
    await settle()

    expect(onMove).not.toHaveBeenCalled()
    expect(created.api!.getFen()).toBe(PLACEMENT)
  })
})
