import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { notFound } from 'next/navigation'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ChessBoardProps } from '@/components/chess-board/chess-board'
import { PageNavItemProvider } from '@/components/layout/page-nav-item'
import {
  answer,
  calls,
  NamedNavItem,
  type Reply,
  renderWithQueries,
} from '@/lib/test-support'

import { GameRoute } from './game-route'
import { COMPUTER_REPLIED_E5, FOOLS_MATE, game, GAME_ID } from './test-games'

// A stand-in for the board, which records what it is given.
const board = vi.hoisted(() => ({
  props: undefined as ChessBoardProps | undefined,
}))
vi.mock('@/components/chess-board/chess-board', () => ({
  ChessBoard: (props: ChessBoardProps) => {
    board.props = props
    return <div role="img" aria-label="Chess board" />
  },
}))

vi.mock('next/navigation', () => ({
  notFound: vi.fn(),
  useParams: () => ({ id: GAME_ID }),
}))
vi.mock('@/lib/page-load', () => ({ loadPage: vi.fn(), reloadPage: vi.fn() }))

const GAME_PATH = `/api/games/${GAME_ID}`
const MOVES_PATH = `${GAME_PATH}/moves`
const RESIGN_PATH = `${GAME_PATH}/resign`

const ok = (body: unknown): Reply => ({ status: 200, body })

// Fool's mate one move from the end: Black to play 2... Qh4#.
const BEFORE_MATE = game({
  fen: 'rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq - 0 2',
  sideToMove: 'black',
  moves: FOOLS_MATE.moves.slice(0, 3),
  legalMoves: { d8: ['e7', 'f6', 'g5', 'h4'] },
})

beforeEach(() => {
  board.props = undefined
  vi.mocked(notFound).mockReset()
})

function renderRoute() {
  return renderWithQueries(
    <PageNavItemProvider>
      <NamedNavItem />
      <GameRoute />
    </PageNavItemProvider>
  )
}

const heading = () => screen.getByRole('heading', { level: 1 })
const highlighted = () => screen.getByRole('status', { name: 'Highlighted' })

describe('GameRoute', () => {
  it('replays a game that is over when its page opens, under My games', async () => {
    answer({ [GAME_PATH]: ok(FOOLS_MATE) })
    renderRoute()

    await waitFor(() => expect(heading()).toHaveTextContent('Replay'))
    expect(highlighted()).toHaveTextContent('/games')
    expect(board.props!.readOnly).toBe(true)
  })

  it('shows the game screen, under Play, for a game still being played, reading it once', async () => {
    const sent = answer({ [GAME_PATH]: ok(game()) })
    renderRoute()

    await waitFor(() => expect(heading()).toHaveTextContent('Your move'))
    expect(highlighted()).toHaveTextContent('/play')
    expect(calls(sent)).toEqual([`GET ${GAME_PATH}`])
    expect(
      screen.queryByRole('button', { name: 'First' })
    ).not.toBeInTheDocument()
  })

  it('keeps the game screen and its result when the game ends there, and replays it when the page opens again', async () => {
    answer({
      [GAME_PATH]: ok(BEFORE_MATE),
      [MOVES_PATH]: ok(FOOLS_MATE),
    })
    const first = renderRoute()
    await waitFor(() => expect(heading()).toHaveTextContent('Your move'))

    act(() => board.props!.onMove!({ from: 'd8', to: 'h4' }))

    await waitFor(() => expect(heading()).toHaveTextContent('You won'))
    expect(screen.getByText('Checkmate.')).toBeInTheDocument()
    expect(highlighted()).toHaveTextContent('/play')

    first.unmount()
    answer({ [GAME_PATH]: ok(FOOLS_MATE) })
    renderRoute()

    await waitFor(() => expect(heading()).toHaveTextContent('Replay'))
  })

  it('keeps the game screen and its result when the player resigns there, and replays the game when the page opens again', async () => {
    const playing = COMPUTER_REPLIED_E5
    const resigned: typeof playing = {
      ...playing,
      status: 'finished',
      result: 'blackWins',
      endReason: 'resignation',
      legalMoves: {},
    }
    answer({ [GAME_PATH]: ok(playing), [RESIGN_PATH]: ok(resigned) })
    const first = renderRoute()
    await waitFor(() => expect(heading()).toHaveTextContent('Your move'))

    fireEvent.click(screen.getByRole('button', { name: 'Resign' }))
    fireEvent.click(
      within(
        screen.getByRole('group', {
          name: 'Resign this game? This cannot be undone.',
        })
      ).getByRole('button', { name: 'Resign' })
    )

    await waitFor(() => expect(heading()).toHaveTextContent('Maia 800 won'))
    expect(screen.getByText('You resigned.')).toBeInTheDocument()

    first.unmount()
    answer({ [GAME_PATH]: ok(resigned) })
    renderRoute()

    await waitFor(() => expect(heading()).toHaveTextContent('Replay'))
    expect(
      screen.getByText(/^You resigned against Maia 800 · /)
    ).toBeInTheDocument()
  })

  it('highlights nothing and shows nothing while the game loads', () => {
    answer({ [GAME_PATH]: new Promise<Reply>(() => {}) })
    renderRoute()

    expect(highlighted()).toHaveTextContent('none')
    expect(
      screen.queryByRole('img', { name: 'Chess board' })
    ).not.toBeInTheDocument()
  })

  it("shows the not-found page for a game the player doesn't have", async () => {
    answer({ [GAME_PATH]: { status: 404, body: { status: 404 } } })
    renderRoute()

    await waitFor(() => expect(notFound).toHaveBeenCalled())
    expect(
      screen.queryByRole('img', { name: 'Chess board' })
    ).not.toBeInTheDocument()
  })
})
