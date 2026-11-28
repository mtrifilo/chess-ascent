import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ChessBoardProps } from '@/components/chess-board/chess-board'
import { PageNavItemProvider } from '@/components/layout/page-nav-item'
import { squaresOf, type Game } from '@/features/game/game-state'
import { game, START_FEN } from '@/features/game/test-games'
import { NamedNavItem } from '@/lib/test-support'

import { ReplayScreen } from './replay-screen'
import { SPECIAL_MOVES_GAME } from './test-replay-game'

// A stand-in for the board, which records what it is given. chess-board's
// own tests cover the real one, a read-only board included.
const board = vi.hoisted(() => ({
  props: undefined as ChessBoardProps | undefined,
}))
vi.mock('@/components/chess-board/chess-board', () => ({
  ChessBoard: (props: ChessBoardProps) => {
    board.props = props
    return <div role="img" aria-label="Chess board" />
  },
}))

beforeEach(() => {
  board.props = undefined
})

const MOVES = SPECIAL_MOVES_GAME.moves
const LAST = MOVES.length

function renderReplay(replayed: Game = SPECIAL_MOVES_GAME) {
  return render(
    <PageNavItemProvider>
      <NamedNavItem />
      <ReplayScreen game={replayed} />
    </PageNavItemProvider>
  )
}

const control = (name: string) => screen.getByRole('button', { name })
const press = (name: string) => fireEvent.click(control(name))
const key = (key: string, init: KeyboardEventInit = {}) =>
  fireEvent.keyDown(document.body, { key, ...init })

// The move the list marks, by its notation.
const marked = () =>
  screen
    .getAllByRole('button')
    .filter(button => button.getAttribute('aria-current') === 'step')
    .map(button => button.textContent)

// Checks that the board shows the step after half-move `step`: the
// position the API sent for that move, with the move marked.
function expectStep(step: number) {
  const move = MOVES[step - 1]
  expect(board.props).toMatchObject({
    fen: move.fen,
    lastMove: squaresOf(move.uci),
  })
  expect(marked()).toEqual([move.san])
}

describe('ReplayScreen', () => {
  it('opens on the last move, showing the final position', () => {
    renderReplay()

    expectStep(LAST)
    expect(board.props!.fen).toBe(SPECIAL_MOVES_GAME.fen)
    // 14. Qxf8# mates the king on d8.
    expect(board.props!.check).toBe('d8')
  })

  it('heads the panel Replay, over how the game ended and when', () => {
    renderReplay()

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Replay'
    )
    expect(
      screen.getByText(/^You won by checkmate · \d+ \w{3} 2026$/)
    ).toBeInTheDocument()
  })

  it('highlights My games', () => {
    renderReplay()

    expect(
      screen.getByRole('status', { name: 'Highlighted' })
    ).toHaveTextContent('/games')
  })

  it('shows a board that cannot be played on, turned to the player', () => {
    renderReplay({ ...SPECIAL_MOVES_GAME, playerSide: 'black' })

    expect(board.props).toMatchObject({ readOnly: true, orientation: 'black' })
    expect(board.props!.destinations).toBeUndefined()
    expect(board.props!.onMove).toBeUndefined()

    press('First')
    expect(board.props).toMatchObject({ readOnly: true, orientation: 'black' })
    expect(board.props!.onMove).toBeUndefined()
  })

  it("shows the API's position at every step, back to the first move and on to the last", () => {
    renderReplay()

    for (let step = LAST - 1; step >= 1; step--) {
      press('Previous')
      expectStep(step)
    }
    for (let step = 2; step <= LAST; step++) {
      press('Next')
      expectStep(step)
    }
  })

  it('walks, above, a game with castling on each side, en passant, a promotion and an under-promotion', () => {
    // The walk through every step above covers each of these moves.
    expect(MOVES.map(move => move.san)).toEqual(
      expect.arrayContaining(['exf6', 'O-O-O', 'O-O', 'gxh8=Q', 'bxa1=N'])
    )
  })

  // "First" is the position after the first move, not the starting
  // position: every step is a move the list can mark.
  it('goes to the first move and to the last', () => {
    renderReplay()

    press('First')
    expect(board.props!.fen).not.toBe(START_FEN)
    expectStep(1)
    press('Last')
    expectStep(LAST)
  })

  it('disables the controls that would go past the last move', () => {
    renderReplay()

    expect(control('Next')).toHaveAttribute('aria-disabled', 'true')
    expect(control('Last')).toHaveAttribute('aria-disabled', 'true')
    expect(control('Previous')).toHaveAttribute('aria-disabled', 'false')
    expect(control('First')).toHaveAttribute('aria-disabled', 'false')
  })

  it('disables the controls that would go before the first move', () => {
    renderReplay()
    press('First')

    expect(control('First')).toHaveAttribute('aria-disabled', 'true')
    expect(control('Previous')).toHaveAttribute('aria-disabled', 'true')
    expect(control('Next')).toHaveAttribute('aria-disabled', 'false')
    expect(control('Last')).toHaveAttribute('aria-disabled', 'false')
  })

  it('keeps focus on a control that can do nothing once pressed, and does nothing more', () => {
    renderReplay()
    press('First')
    const first = control('First')
    first.focus()

    fireEvent.click(first)
    fireEvent.click(control('Previous'))

    expect(document.activeElement).toBe(first)
    expectStep(1)
  })

  it('jumps to a move clicked in the list', () => {
    renderReplay()
    const list = screen.getByRole('table')

    fireEvent.click(within(list).getByRole('button', { name: 'O-O-O' }))
    expectStep(12)
    fireEvent.click(within(list).getByRole('button', { name: 'e4' }))
    expectStep(1)
    fireEvent.click(within(list).getByRole('button', { name: 'bxa1=N' }))
    expectStep(22)
  })

  it('steps back and forward with the left and right arrow keys', () => {
    renderReplay()

    key('ArrowLeft')
    expectStep(LAST - 1)
    key('ArrowLeft')
    expectStep(LAST - 2)
    key('ArrowRight')
    expectStep(LAST - 1)
  })

  it('stops the arrow keys at either end', () => {
    renderReplay()

    key('ArrowRight')
    expectStep(LAST)
    press('First')
    key('ArrowLeft')
    expectStep(1)
  })

  it('steps with the arrow keys when a control has focus', () => {
    renderReplay()
    control('First').focus()

    fireEvent.keyDown(control('First'), { key: 'ArrowLeft' })

    expectStep(LAST - 1)
  })

  it('leaves the arrow keys to a field that has focus', () => {
    renderReplay()
    const field = document.createElement('input')
    document.body.append(field)
    try {
      fireEvent.keyDown(field, { key: 'ArrowLeft' })

      expectStep(LAST)
    } finally {
      field.remove()
    }
  })

  it('leaves the arrow keys to an open menu', () => {
    renderReplay()
    const menu = document.createElement('div')
    menu.setAttribute('role', 'menu')
    const item = document.createElement('div')
    item.setAttribute('role', 'menuitem')
    menu.append(item)
    document.body.append(menu)
    try {
      fireEvent.keyDown(item, { key: 'ArrowLeft' })

      expectStep(LAST)
    } finally {
      menu.remove()
    }
  })

  it('leaves an arrow key held with a modifier to the browser', () => {
    renderReplay()

    key('ArrowLeft', { altKey: true })
    key('ArrowLeft', { metaKey: true })

    expectStep(LAST)
  })

  it('ends a resigned game on the position it was resigned in', () => {
    // The game as if Black had resigned after 7. O-O: the moves and
    // positions the API sent up to there.
    const resigned: Game = {
      ...SPECIAL_MOVES_GAME,
      playerSide: 'black',
      fen: MOVES[12].fen,
      sideToMove: 'black',
      result: 'whiteWins',
      endReason: 'resignation',
      moves: MOVES.slice(0, 13),
    }
    renderReplay(resigned)

    expect(board.props).toMatchObject({
      fen: MOVES[12].fen,
      lastMove: squaresOf(MOVES[12].uci),
      orientation: 'black',
    })
    expect(marked()).toEqual(['O-O'])
    expect(control('Next')).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByText(/^You resigned · /)).toBeInTheDocument()
  })

  it('shows the starting position, with nothing to step to, for a game without moves', () => {
    renderReplay(
      game({
        status: 'finished',
        result: 'blackWins',
        endReason: 'resignation',
        legalMoves: {},
      })
    )

    expect(board.props).toMatchObject({ fen: START_FEN, readOnly: true })
    expect(board.props!.lastMove).toBeUndefined()
    for (const name of ['First', 'Previous', 'Next', 'Last'])
      expect(control(name)).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByText('No moves yet.')).toBeInTheDocument()
  })
})
