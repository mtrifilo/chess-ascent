import { act, screen, waitFor } from '@testing-library/react'
import { notFound } from 'next/navigation'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ChessBoardProps } from '@/components/chess-board/chess-board'
import {
  PageNavItemProvider,
  usePageNavItem,
} from '@/components/layout/page-nav-item'
import { answer, type Reply, renderWithQueries } from '@/lib/test-support'

import { GameRoute } from './game-route'
import { FOOLS_MATE, game, GAME_ID } from './test-games'

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

// Shows the navigation item the page names, as the top bar would.
function NamedNavItem() {
  return <output aria-label="Highlighted">{usePageNavItem() ?? 'none'}</output>
}

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

  it('shows the game screen, under Play, for a game still being played', async () => {
    answer({ [GAME_PATH]: ok(game()) })
    renderRoute()

    await waitFor(() => expect(heading()).toHaveTextContent('Your move'))
    expect(highlighted()).toHaveTextContent('/play')
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
