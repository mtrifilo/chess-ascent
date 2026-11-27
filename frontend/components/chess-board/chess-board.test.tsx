import { Chessground } from '@lichess-org/chessground'
import type { Config } from '@lichess-org/chessground/config'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ChessBoard, type ChessBoardProps, type Square } from './chess-board'

// These tests check what the wrapper passes to chessground and when, through
// a stand-in for chessground's API. chess-board.render.test.tsx renders the
// real board.
// `state.turnColor` is the side chessground gives the move: the side to move
// in the position, until the person moves.
const api = vi.hoisted(() => ({
  state: { turnColor: 'white' },
  set: vi.fn(),
  setAutoShapes: vi.fn(),
  cancelMove: vi.fn(),
  destroy: vi.fn(),
}))

vi.mock('@lichess-org/chessground', () => ({
  Chessground: vi.fn(() => api),
}))

// The main position of the designs: White to move, after 5... d6.
const FEN =
  'r1bqk2r/ppp2ppp/2np1n2/2b1p3/2B1P3/2PP1N2/PP3PPP/RNBQK2R w KQkq - 0 6'
const BLACK_TO_MOVE =
  'rnbqkbnr/pppppppp/8/8/4P3/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1'

// The knight on f3 is picked up in the designs.
const DESTINATIONS = new Map<Square, Square[]>([
  ['f3', ['e5', 'g5', 'd4', 'h4', 'd2', 'g1']],
])

const props = (overrides: Partial<ChessBoardProps> = {}): ChessBoardProps => ({
  fen: FEN,
  destinations: DESTINATIONS,
  lastMove: { from: 'd7', to: 'd6' },
  ...overrides,
})

function createdWith(): Config {
  const calls = vi.mocked(Chessground).mock.calls
  return calls[calls.length - 1][1] as Config
}

beforeEach(() => {
  vi.clearAllMocks()
  api.state.turnColor = 'white'
})

describe('ChessBoard', () => {
  it('is named as one image for assistive technology', () => {
    render(<ChessBoard {...props()} />)

    expect(screen.getByRole('img', { name: 'Chess board' })).toBeInTheDocument()
  })

  it('creates the board once with the position and its legal moves', () => {
    render(<ChessBoard {...props()} />)

    expect(Chessground).toHaveBeenCalledTimes(1)
    const config = createdWith()
    expect(config.fen).toBe(FEN)
    expect(config.orientation).toBe('white')
    expect(config.turnColor).toBe('white')
    expect(config.lastMove).toEqual(['d7', 'd6'])
    expect(config.movable?.color).toBe('white')
    expect(config.movable?.dests).toEqual(DESTINATIONS)
  })

  it('accepts no move but the destinations given', () => {
    render(<ChessBoard {...props()} />)

    const config = createdWith()
    expect(config.movable?.free).toBe(false)
    expect(config.movable?.showDests).toBe(true)
    expect(config.premovable?.enabled).toBe(false)
    expect(config.drawable?.enabled).toBe(false)
  })

  it('lets nothing move when no destinations are given', () => {
    render(<ChessBoard {...props({ destinations: undefined })} />)

    expect(createdWith().movable?.dests).toEqual(new Map())
  })

  it('lets the side to move in the position move, and shows either side at the bottom', () => {
    render(
      <ChessBoard {...props({ fen: BLACK_TO_MOVE, orientation: 'black' })} />
    )

    const config = createdWith()
    expect(config.orientation).toBe('black')
    expect(config.turnColor).toBe('black')
    expect(config.movable?.color).toBe('black')
  })

  it('marks the king on the square given as in check', () => {
    render(
      <ChessBoard
        {...props({
          fen: 'rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3',
          check: 'e1',
        })}
      />
    )

    expect(createdWith().check).toBe('white')
  })

  it.each([
    ['no square', undefined],
    ['an empty square', 'e6'],
    ['a square without a king', 'e4'],
  ] as const)('marks no check given %s', (_, check) => {
    render(<ChessBoard {...props({ check })} />)

    expect(createdWith().check).toBe(false)
  })

  it('draws the arrows and circles given', () => {
    render(
      <ChessBoard
        {...props({ arrows: [{ from: 'b1', to: 'd2' }], circles: ['b1'] })}
      />
    )

    expect(createdWith().drawable?.autoShapes).toEqual([
      { orig: 'b1', dest: 'd2', brush: 'arrow' },
      { orig: 'b1', brush: 'circle' },
    ])
  })

  it('reports a move as from-square and to-square, to the latest onMove', () => {
    const first = vi.fn()
    const latest = vi.fn()
    const { rerender } = render(<ChessBoard {...props({ onMove: first })} />)
    rerender(<ChessBoard {...props({ onMove: latest })} />)

    createdWith().movable?.events?.after?.('f3', 'g5', { premove: false })

    expect(first).not.toHaveBeenCalled()
    expect(latest).toHaveBeenCalledWith({ from: 'f3', to: 'g5' })
  })

  it('leaves the board alone when re-rendered with equal inputs in new objects', () => {
    const shapes = () => ({
      arrows: [{ from: 'b1', to: 'd2' }] as const,
      circles: ['b1'] as const,
    })
    const { rerender } = render(<ChessBoard {...props(shapes())} />)
    api.set.mockClear()
    api.setAutoShapes.mockClear()

    rerender(
      <ChessBoard
        {...props({
          ...shapes(),
          destinations: new Map(DESTINATIONS),
          lastMove: { from: 'd7', to: 'd6' },
          onMove: vi.fn(),
        })}
      />
    )

    expect(Chessground).toHaveBeenCalledTimes(1)
    expect(api.set).not.toHaveBeenCalled()
    expect(api.setAutoShapes).not.toHaveBeenCalled()
  })

  it('ignores fields of the last move that it does not use', () => {
    const { rerender } = render(<ChessBoard {...props()} />)
    api.set.mockClear()
    const fromServer = { from: 'd7', to: 'd6', at: 1 } as const

    rerender(<ChessBoard {...props({ lastMove: fromServer })} />)

    expect(api.set).not.toHaveBeenCalled()
  })

  it('redraws the pieces for a new position, with its legal moves and last move', () => {
    const { rerender } = render(<ChessBoard {...props()} />)
    api.set.mockClear()

    rerender(
      <ChessBoard
        fen={BLACK_TO_MOVE}
        destinations={new Map([['e7', ['e6', 'e5']]])}
        lastMove={{ from: 'e2', to: 'e4' }}
      />
    )

    expect(api.set).toHaveBeenCalledWith({
      fen: BLACK_TO_MOVE,
      turnColor: 'black',
      lastMove: ['e2', 'e4'],
      check: false,
      movable: { color: 'black', dests: new Map([['e7', ['e6', 'e5']]]) },
    })
  })

  // After a move, chessground already shows it; resending the old FEN would
  // put the piece back while the move is being sent.
  it.each([
    ['the destinations', { destinations: new Map() }],
    ['the last move', { lastMove: { from: 'f3', to: 'g5' } }],
    ['read-only', { readOnly: true }],
  ] as const)(
    'passes a change to %s on without redrawing the pieces',
    (_, change) => {
      const { rerender } = render(<ChessBoard {...props()} />)
      api.set.mockClear()

      rerender(<ChessBoard {...props(change)} />)

      expect(api.set).toHaveBeenCalledTimes(1)
      expect(api.set.mock.calls[0][0]).not.toHaveProperty('fen')
    }
  )

  it('holds the other position inputs back after a move, until the position changes', () => {
    const { rerender } = render(<ChessBoard {...props()} />)
    // The person moves: chessground passes the turn.
    api.state.turnColor = 'black'
    api.set.mockClear()

    rerender(
      <ChessBoard
        {...props({ lastMove: { from: 'f3', to: 'g5' }, readOnly: true })}
      />
    )
    expect(api.set).not.toHaveBeenCalled()

    rerender(
      <ChessBoard
        {...props({ fen: BLACK_TO_MOVE, lastMove: { from: 'f3', to: 'g5' } })}
      />
    )
    expect(api.set).toHaveBeenCalledWith(
      expect.objectContaining({ fen: BLACK_TO_MOVE })
    )
  })

  it('drops a piece the person has picked up when read-only turns on', () => {
    const { rerender } = render(<ChessBoard {...props()} />)

    rerender(<ChessBoard {...props({ readOnly: true })} />)

    expect(api.cancelMove).toHaveBeenCalledTimes(1)
  })

  it('clears the last move when it is no longer given', () => {
    const { rerender } = render(<ChessBoard {...props()} />)
    api.set.mockClear()

    rerender(<ChessBoard {...props({ lastMove: undefined })} />)

    expect(api.set).toHaveBeenCalledWith(
      expect.objectContaining({ lastMove: undefined })
    )
  })

  it('flips the board when the orientation changes', () => {
    const { rerender } = render(<ChessBoard {...props()} />)
    api.set.mockClear()

    rerender(<ChessBoard {...props({ orientation: 'black' })} />)

    expect(api.set).toHaveBeenCalledWith({ orientation: 'black' })
    expect(Chessground).toHaveBeenCalledTimes(1)
  })

  it('redraws the shapes when the arrows or circles change', () => {
    const { rerender } = render(<ChessBoard {...props()} />)
    api.setAutoShapes.mockClear()

    rerender(<ChessBoard {...props({ circles: ['b1'] })} />)

    expect(api.setAutoShapes).toHaveBeenCalledWith([
      { orig: 'b1', brush: 'circle' },
    ])
  })

  it('gives no side the move and no destinations when read-only', () => {
    render(<ChessBoard {...props({ readOnly: true })} />)

    expect(createdWith().movable?.color).toBeUndefined()
    expect(createdWith().movable?.dests).toEqual(new Map())
  })

  it('turns read-only on and off without creating the board again', () => {
    const { rerender } = render(<ChessBoard {...props()} />)
    api.set.mockClear()

    rerender(<ChessBoard {...props({ readOnly: true })} />)
    rerender(<ChessBoard {...props()} />)

    expect(Chessground).toHaveBeenCalledTimes(1)
    expect(api.destroy).not.toHaveBeenCalled()
    expect(api.set).toHaveBeenLastCalledWith(
      expect.objectContaining({
        movable: { color: 'white', dests: DESTINATIONS },
      })
    )
  })

  it('ends any move in progress, then destroys the board, on unmount', () => {
    const { unmount } = render(<ChessBoard {...props()} />)

    unmount()

    expect(api.cancelMove).toHaveBeenCalledTimes(1)
    expect(api.destroy).toHaveBeenCalledTimes(1)
    expect(api.cancelMove.mock.invocationCallOrder[0]).toBeLessThan(
      api.destroy.mock.invocationCallOrder[0]
    )
  })
})
