import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { onlineManager } from '@tanstack/react-query'
import { notFound } from 'next/navigation'
import { Activity } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { ChessBoardProps } from '@/components/chess-board/chess-board'
import {
  PageNavItemProvider,
  usePageNavItem,
} from '@/components/layout/page-nav-item'
import {
  answer,
  calls,
  type Reply,
  renderWithQueries,
} from '@/lib/test-support'

import { GameScreen } from './game-screen'
import {
  AFTER_E4,
  FOOLS_MATE,
  game,
  GAME_ID,
  move,
  PROMOTION_GAME,
} from './test-games'

// A stand-in for the board, which records what it is given and how many
// times it has been mounted. chess-board's own tests cover the real one.
const board = vi.hoisted(() => ({
  props: undefined as ChessBoardProps | undefined,
  mounts: 0,
}))
vi.mock('@/components/chess-board/chess-board', async () => {
  const { useEffect } = await import('react')
  return {
    ChessBoard: (props: ChessBoardProps) => {
      board.props = props
      useEffect(() => {
        board.mounts += 1
      }, [])
      return <div role="img" aria-label="Chess board" />
    },
  }
})

vi.mock('next/navigation', () => ({ notFound: vi.fn(), useRouter: vi.fn() }))
vi.mock('@/lib/page-load', () => ({ loadPage: vi.fn(), reloadPage: vi.fn() }))

const GAME_PATH = `/api/games/${GAME_ID}`
const MOVES_PATH = `${GAME_PATH}/moves`

const ok = (body: unknown): Reply => ({ status: 200, body })
const problem = (status: number): Reply => ({ status, body: { status } })

beforeEach(() => {
  board.props = undefined
  board.mounts = 0
  vi.mocked(notFound).mockReset()
})

// Shows the navigation item the page names, as the top bar would.
function NamedNavItem() {
  return <output aria-label="Highlighted">{usePageNavItem() ?? 'none'}</output>
}

async function renderGame() {
  const result = renderWithQueries(
    <PageNavItemProvider>
      <NamedNavItem />
      <GameScreen id={GAME_ID} />
    </PageNavItemProvider>
  )
  await screen.findByRole('img', { name: 'Chess board' })
  return result
}

// Plays a move as the person would on the board, which reports it through
// onMove; React is let render what that changes.
function playOnBoard(from: string, to: string) {
  act(() =>
    board.props!.onMove!({ from, to } as Parameters<
      NonNullable<ChessBoardProps['onMove']>
    >[0])
  )
}

// The answer of a server that cannot be reached: fetch fails. The failure is
// marked handled at once, since the test hands it over before any request.
function unreachable(): Promise<Reply> {
  const failure = Promise.reject(new TypeError('Failed to fetch'))
  failure.catch(() => {})
  return failure
}

// The bodies of the moves posted.
const sentMoves = (sent: ReturnType<typeof answer>) =>
  sent
    .filter(request => request.call === `POST ${MOVES_PATH}`)
    .map(request => request.body)

describe('GameScreen', () => {
  it("shows the position, whose move it is, and the player's colour at the bottom", async () => {
    answer({ [GAME_PATH]: ok(game()) })
    await renderGame()

    expect(board.props).toMatchObject({
      fen: game().fen,
      orientation: 'white',
      readOnly: false,
      check: undefined,
      lastMove: undefined,
    })
    expect(board.props!.destinations).toEqual(
      new Map([
        ['e2', ['e3', 'e4']],
        ['g1', ['f3', 'h3']],
      ])
    )
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Your move'
    )
    expect(screen.getByText('White to move · move 1')).toBeInTheDocument()
    expect(screen.getByText('No moves yet.')).toBeInTheDocument()
  })

  it('calls the player "You" on both bars when they move both sides', async () => {
    answer({ [GAME_PATH]: ok(game()) })
    await renderGame()

    expect(screen.getAllByText('You')).toHaveLength(2)
    expect(screen.queryByText(/Maia/)).not.toBeInTheDocument()
  })

  it('has no Hint or Resign control', async () => {
    answer({ [GAME_PATH]: ok(game()) })
    await renderGame()

    expect(screen.queryByText('Hint')).not.toBeInTheDocument()
    expect(screen.queryByText('Resign')).not.toBeInTheDocument()
  })

  it('highlights Play while the game is being played', async () => {
    answer({ [GAME_PATH]: ok(game()) })
    await renderGame()

    expect(
      screen.getByRole('status', { name: 'Highlighted' })
    ).toHaveTextContent('/play')
  })

  it('sends a move in UCI form and shows the position the API answers with', async () => {
    const sent = answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: ok(AFTER_E4) })
    await renderGame()

    playOnBoard('e2', 'e4')

    await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4.fen))
    expect(sentMoves(sent)).toEqual([{ uci: 'e2e4', halfMoveNumber: 1 }])
    expect(board.props!.lastMove).toEqual({ from: 'e2', to: 'e4' })
    expect(screen.getByText('Black to move · move 1')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'e4' })).toHaveAttribute(
      'aria-current',
      'step'
    )
    // The board showed the move itself, so it is not mounted again.
    expect(board.mounts).toBe(1)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('sends each move as the half-move the screen expects it to be', async () => {
    const sent = answer({ [GAME_PATH]: ok(AFTER_E4), [MOVES_PATH]: ok(game()) })
    await renderGame()

    // One move has been played, so Black's reply is the second.
    playOnBoard('e7', 'e5')

    await waitFor(() =>
      expect(sentMoves(sent)).toEqual([{ uci: 'e7e5', halfMoveNumber: 2 }])
    )
  })

  it('counts the half-move on from each move played on the screen', async () => {
    answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: ok(AFTER_E4) })
    await renderGame()
    playOnBoard('e2', 'e4')
    await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4.fen))

    const sent = answer({ [GAME_PATH]: ok(AFTER_E4), [MOVES_PATH]: ok(game()) })
    playOnBoard('e7', 'e5')

    await waitFor(() =>
      expect(sentMoves(sent)).toEqual([{ uci: 'e7e5', halfMoveNumber: 2 }])
    )
  })

  it('lets nothing move while a move is being sent', async () => {
    let settle!: (reply: Reply) => void
    answer({
      [GAME_PATH]: ok(game()),
      [MOVES_PATH]: new Promise<Reply>(resolve => (settle = resolve)),
    })
    await renderGame()

    playOnBoard('e2', 'e4')

    await waitFor(() => expect(board.props!.readOnly).toBe(true))
    settle(ok(AFTER_E4))
    await waitFor(() => expect(board.props!.readOnly).toBe(false))
  })

  it('marks the king in check, read from the latest move', async () => {
    const check = game({
      fen: 'rnbqkbnr/ppp2ppp/3p4/1B2p3/4P3/8/PPPP1PPP/RNBQK1NR b KQkq - 1 3',
      sideToMove: 'black',
      moves: [
        move(1, 'e2e4', 'e4'),
        move(2, 'e7e5', 'e5'),
        move(3, 'd7d6', 'd6'),
        move(4, 'f1b5', 'Bb5+'),
      ],
    })
    answer({ [GAME_PATH]: ok(check) })
    await renderGame()

    expect(board.props!.check).toBe('e8')
  })

  describe('promotion', () => {
    it('asks for the piece, then sends the move with its letter', async () => {
      const sent = answer({
        [GAME_PATH]: ok(PROMOTION_GAME),
        [MOVES_PATH]: ok(game()),
      })
      await renderGame()

      playOnBoard('e7', 'e8')

      const picker = screen.getByRole('group', {
        name: 'Choose a piece for the pawn on e8.',
      })
      expect(
        screen.getByText('Choose a piece for the pawn on e8.')
      ).toBeInTheDocument()
      expect(sentMoves(sent)).toEqual([])

      fireEvent.click(within(picker).getByRole('button', { name: 'Knight' }))

      await waitFor(() =>
        expect(sentMoves(sent)).toEqual([{ uci: 'e7e8n', halfMoveNumber: 1 }])
      )
      expect(screen.queryByRole('group')).not.toBeInTheDocument()
    })

    it('drops the choice when a new position arrives meanwhile', async () => {
      const sent = answer({ [GAME_PATH]: ok(PROMOTION_GAME) })
      const { queryClient } = await renderGame()

      playOnBoard('e7', 'e8')
      expect(screen.getByRole('group')).toBeInTheDocument()
      // Another tab played a move, and this screen read the game again.
      act(() => queryClient.setQueryData(['game', GAME_ID], AFTER_E4))

      await waitFor(() =>
        expect(screen.getByText('Black to move · move 1')).toBeInTheDocument()
      )
      expect(screen.queryByRole('group')).not.toBeInTheDocument()
      expect(sentMoves(sent)).toEqual([])
    })

    it('does not ask for a move that does not promote', async () => {
      const sent = answer({
        [GAME_PATH]: ok(PROMOTION_GAME),
        [MOVES_PATH]: ok(PROMOTION_GAME),
      })
      await renderGame()

      playOnBoard('e1', 'd1')

      expect(screen.queryByRole('group')).not.toBeInTheDocument()
      await waitFor(() =>
        expect(sentMoves(sent)).toEqual([{ uci: 'e1d1', halfMoveNumber: 1 }])
      )
    })
  })

  describe('a move that is not played', () => {
    // The query cache reports each failure to the console.
    beforeEach(() => {
      vi.spyOn(console, 'error').mockImplementation(() => {})
    })
    afterEach(() => {
      vi.mocked(console.error).mockRestore()
    })

    it.each([
      ['refuses it', () => problem(422)],
      ['fails', () => problem(500)],
      ['has no reply from a computer opponent', () => problem(503)],
      ['cannot be reached', unreachable],
    ])(
      'puts the board back and shows the error when the API %s',
      async (_, reply) => {
        answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: reply() })
        await renderGame()

        playOnBoard('e2', 'e4')

        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent('Your move was not sent')
        expect(alert).toHaveTextContent(
          'The connection to the server was lost. The board shows the position before your move.'
        )
        expect(board.mounts).toBe(2)
        expect(board.props!.fen).toBe(game().fen)
      }
    )

    it('shows the error when the move fails while Next.js has the page hidden', async () => {
      let settle!: (reply: Reply) => void
      answer({
        [GAME_PATH]: ok(game()),
        [MOVES_PATH]: new Promise<Reply>(resolve => (settle = resolve)),
      })
      const screenIn = (mode: 'visible' | 'hidden') => (
        <PageNavItemProvider>
          <Activity mode={mode}>
            <GameScreen id={GAME_ID} />
          </Activity>
        </PageNavItemProvider>
      )
      const { rerender } = renderWithQueries(screenIn('visible'))
      await screen.findByRole('img', { name: 'Chess board' })
      playOnBoard('e2', 'e4')

      // The player leaves before the answer comes, and it is a failure.
      rerender(screenIn('hidden'))
      settle(problem(500))
      await waitFor(() => expect(console.error).toHaveBeenCalled())
      rerender(screenIn('visible'))

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Your move was not sent'
      )
      expect(board.props!.fen).toBe(game().fen)
    })

    it('sends the same move again on "Try again"', async () => {
      answer({
        [GAME_PATH]: ok(game()),
        [MOVES_PATH]: problem(500),
      })
      await renderGame()
      playOnBoard('e2', 'e4')
      await screen.findByRole('alert')

      const retried = answer({
        [GAME_PATH]: ok(game()),
        [MOVES_PATH]: ok(AFTER_E4),
      })
      fireEvent.click(screen.getByRole('button', { name: 'Try again' }))

      await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4.fen))
      expect(sentMoves(retried)).toEqual([{ uci: 'e2e4', halfMoveNumber: 1 }])
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('reads the game again, and shows where it stands, when the move was saved but its answer was lost', async () => {
      answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: unreachable() })
      await renderGame()
      const sent = answer({
        [GAME_PATH]: ok(AFTER_E4),
        [MOVES_PATH]: unreachable(),
      })

      playOnBoard('e2', 'e4')

      await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4.fen))
      expect(calls(sent)).toEqual([`POST ${MOVES_PATH}`, `GET ${GAME_PATH}`])
      // The error was about the position before; the board now shows the
      // position the API holds.
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('fails at once, rather than waiting, while the browser is offline', async () => {
      answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: unreachable() })
      await renderGame()
      onlineManager.setOnline(false)

      try {
        playOnBoard('e2', 'e4')

        expect(await screen.findByRole('alert')).toHaveTextContent(
          'Your move was not sent'
        )
        expect(board.mounts).toBe(2)
      } finally {
        onlineManager.setOnline(true)
      }
    })
  })

  describe('a move the API finds out of date', () => {
    it('loads the game again, without the error', async () => {
      answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: problem(409) })
      await renderGame()
      // Another tab played 1. e4 meanwhile.
      const sent = answer({
        [GAME_PATH]: ok(AFTER_E4),
        [MOVES_PATH]: problem(409),
      })

      playOnBoard('g1', 'f3')

      await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4.fen))
      expect(board.mounts).toBe(2)
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(calls(sent)).toEqual([`POST ${MOVES_PATH}`, `GET ${GAME_PATH}`])
    })

    it('sends the next move as the half-move after the game it loaded', async () => {
      answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: problem(409) })
      await renderGame()
      answer({ [GAME_PATH]: ok(AFTER_E4), [MOVES_PATH]: problem(409) })
      playOnBoard('g1', 'f3')
      await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4.fen))

      const sent = answer({
        [GAME_PATH]: ok(AFTER_E4),
        [MOVES_PATH]: ok(game()),
      })
      playOnBoard('e7', 'e5')

      await waitFor(() =>
        expect(sentMoves(sent)).toEqual([{ uci: 'e7e5', halfMoveNumber: 2 }])
      )
    })

    it('shows no error for a signed-out visitor, whom the API client sends to sign in', async () => {
      const sent = answer({
        [GAME_PATH]: ok(game()),
        [MOVES_PATH]: problem(401),
      })
      await renderGame()

      playOnBoard('e2', 'e4')

      await waitFor(() => expect(sentMoves(sent)).toHaveLength(1))
      await waitFor(() => expect(board.props!.readOnly).toBe(false))
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(board.mounts).toBe(1)
    })
  })

  describe('a finished game', () => {
    it('shows the result and its reason instead of whose move it is', async () => {
      answer({ [GAME_PATH]: ok(FOOLS_MATE) })
      await renderGame()

      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
        'You won'
      )
      expect(screen.getByText('Checkmate.')).toBeInTheDocument()
      expect(
        screen.getByRole('button', { name: 'New game' })
      ).toBeInTheDocument()
      expect(screen.queryByText('Your move')).not.toBeInTheDocument()
    })

    it('marks the mated king and lets nothing move', async () => {
      answer({ [GAME_PATH]: ok(FOOLS_MATE) })
      await renderGame()

      expect(board.props).toMatchObject({ check: 'e1', readOnly: true })
    })

    it('highlights My games', async () => {
      answer({ [GAME_PATH]: ok(FOOLS_MATE) })
      await renderGame()

      expect(
        screen.getByRole('status', { name: 'Highlighted' })
      ).toHaveTextContent('/games')
    })

    it.each([
      ['draw', 'stalemate', 'Draw', 'Stalemate.'],
      ['draw', 'threefoldRepetition', 'Draw', 'Threefold repetition.'],
      ['draw', 'fiftyMoveRule', 'Draw', 'Fifty-move rule.'],
      ['draw', 'deadPosition', 'Draw', 'Neither side can checkmate.'],
      ['whiteWins', 'checkmate', 'You won', 'Checkmate.'],
    ] as const)(
      'words a %s by %s as "%s %s"',
      async (result, endReason, title, reason) => {
        answer({ [GAME_PATH]: ok({ ...FOOLS_MATE, result, endReason }) })
        await renderGame()

        expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
          title
        )
        expect(screen.getByText(reason)).toBeInTheDocument()
      }
    )
  })

  it('flips the board and the players with it', async () => {
    answer({ [GAME_PATH]: ok(game({ playerSide: 'black' })) })
    await renderGame()
    expect(board.props!.orientation).toBe('black')

    fireEvent.click(screen.getByRole('button', { name: 'Flip board' }))

    expect(board.props!.orientation).toBe('white')
    fireEvent.click(screen.getByRole('button', { name: 'Flip board' }))
    expect(board.props!.orientation).toBe('black')
  })

  it("shows the not-found page for a game the player doesn't have", async () => {
    answer({ [GAME_PATH]: problem(404) })
    renderWithQueries(<GameScreen id={GAME_ID} />)

    await waitFor(() => expect(notFound).toHaveBeenCalled())
    expect(
      screen.queryByRole('img', { name: 'Chess board' })
    ).not.toBeInTheDocument()
  })
})
