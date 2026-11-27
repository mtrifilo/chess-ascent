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
const WHITE_MOVES = new Map([
  ['f3', ['g5']],
  ['a2', ['a3']],
] as const)
// The position after 6. Ng5, Black to move.
const AFTER_NG5 =
  'r1bqk2r/ppp2ppp/2np1n2/2b1p1N1/2B1P3/2PP4/PP3PPP/RNBQK2R b KQkq - 1 6'

// Renders the board and returns chessground's root element.
function renderBoard(props: Partial<ChessBoardProps> = {}) {
  const { container } = render(<ChessBoard fen={FEN} {...props} />)
  const wrap = container.querySelector('.cg-wrap')
  if (!wrap) throw new Error('chessground did not render')
  return wrap
}

// The squares of chessground's piece or square elements, sorted. Chessground
// keeps each element's square in its `cgKey` property.
function squaresOf(elements: NodeListOf<Element>) {
  return [...elements]
    .map(element => (element as Element & { cgKey: string }).cgKey)
    .sort()
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

  it('fills the hinted squares, and only those', () => {
    const board = renderBoard({ hintSquares: ['b1', 'd2'] })

    expect(squaresOf(board.querySelectorAll('square.hint'))).toEqual([
      'b1',
      'd2',
    ])
  })

  // Playing as Black after 1. e4: e2 is empty. The board must draw exactly
  // the FEN's pieces after a new position and after a flip.
  it('draws exactly the pieces of each position, in either orientation', async () => {
    const afterE4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1'
    const afterE5 =
      'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2'
    const { container, rerender } = render(
      <ChessBoard fen={afterE4} orientation="black" />
    )
    const drawn = () =>
      squaresOf(container.querySelectorAll('cg-board piece:not(.ghost)'))
    const expectPieces = (squares: string[]) =>
      vi.waitFor(() => expect(drawn()).toEqual([...squares].sort()))

    const startSquares = [
      ...'abcdefgh'.split('').flatMap(file => [1, 2, 7, 8].map(r => file + r)),
    ]
    const afterE4Squares = startSquares.filter(s => s !== 'e2').concat('e4')
    await expectPieces(afterE4Squares)

    rerender(<ChessBoard fen={afterE5} orientation="black" />)
    await expectPieces(afterE4Squares.filter(s => s !== 'e7').concat('e5'))

    rerender(<ChessBoard fen={afterE5} orientation="white" />)
    await expectPieces(afterE4Squares.filter(s => s !== 'e7').concat('e5'))
    expect(drawn()).toHaveLength(32)
    expect(drawn()).not.toContain('e2')
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

  // The screen sends a move to the server and waits for the new position; in
  // that time the board must not let the same side move again.
  it.each<[string, Partial<ChessBoardProps>]>([
    ['the last move changes', { lastMove: { from: 'f3', to: 'g5' } }],
    ['the king is marked in check', { check: 'e1' }],
    ['the destinations change', { destinations: new Map([['a2', ['a3']]]) }],
    ['read-only turns on', { readOnly: true }],
  ])('lets nothing more move after a move when %s', async (_, change) => {
    const onMove = vi.fn()
    const board = (props: Partial<ChessBoardProps>) => (
      <ChessBoard
        fen={FEN}
        destinations={WHITE_MOVES}
        onMove={onMove}
        {...props}
      />
    )
    const { rerender } = render(board({}))
    tap('f3', 'g5')
    await settle()

    rerender(board(change))
    rerender(board({}))
    tap('a2', 'a3')
    await settle()

    expect(onMove).toHaveBeenCalledTimes(1)
    expect(onMove).toHaveBeenCalledWith({ from: 'f3', to: 'g5' })
  })

  // Chessground shows a move at once but reports it on a timer; an update in
  // between must be held back too.
  it('lets nothing more move when the inputs change before the move is reported', async () => {
    const onMove = vi.fn()
    const board = (props: Partial<ChessBoardProps>) => (
      <ChessBoard
        fen={FEN}
        destinations={WHITE_MOVES}
        onMove={onMove}
        {...props}
      />
    )
    const { rerender } = render(board({}))
    tap('f3', 'g5')

    rerender(board({ check: 'e1' }))
    await settle()
    rerender(board({}))
    tap('a2', 'a3')
    await settle()

    expect(onMove).toHaveBeenCalledTimes(1)
    expect(onMove).toHaveBeenCalledWith({ from: 'f3', to: 'g5' })
  })

  it('lets the next side move once the new position arrives', async () => {
    const onMove = vi.fn()
    const { rerender } = render(
      <ChessBoard fen={FEN} destinations={WHITE_MOVES} onMove={onMove} />
    )
    tap('f3', 'g5')
    await settle()

    rerender(
      <ChessBoard
        fen={AFTER_NG5}
        destinations={new Map([['h7', ['h6']]])}
        onMove={onMove}
      />
    )
    tap('h7', 'h6')
    await settle()

    expect(onMove).toHaveBeenLastCalledWith({ from: 'h7', to: 'h6' })
  })

  it('drops a selected piece when read-only turns on', async () => {
    const onMove = vi.fn()
    const board = (readOnly: boolean) => (
      <ChessBoard
        fen={FEN}
        destinations={KNIGHT_MOVES}
        onMove={onMove}
        readOnly={readOnly}
      />
    )
    const { rerender } = render(board(false))
    tap('f3')

    rerender(board(true))
    rerender(board(false))
    tap('g5')
    await settle()

    expect(onMove).not.toHaveBeenCalled()
  })

  it.each([
    ['playable', [false], 1],
    ['read-only', [true], 0],
    ['read-only, then playable again', [true, false], 1],
  ] as const)(
    'passes touches and clicks to chessground only when playable (%s)',
    (_, readOnlyRenders, reached) => {
      const { container, rerender } = render(
        <ChessBoard fen={FEN} readOnly={readOnlyRenders[0]} />
      )
      for (const readOnly of readOnlyRenders.slice(1))
        rerender(<ChessBoard fen={FEN} readOnly={readOnly} />)
      const board = container.querySelector('cg-board')!
      const listener = vi.fn()
      board.addEventListener('touchstart', listener)
      board.addEventListener('mousedown', listener)

      board.dispatchEvent(new Event('touchstart', { bubbles: true }))
      board.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))

      expect(listener).toHaveBeenCalledTimes(reached * 2)
    }
  )

  it('lets nothing move when read-only', async () => {
    const onMove = vi.fn()
    renderBoard({ destinations: KNIGHT_MOVES, onMove, readOnly: true })

    tap('f3', 'g5')
    await settle()

    expect(onMove).not.toHaveBeenCalled()
    expect(created.api!.getFen()).toBe(PLACEMENT)
  })
})
