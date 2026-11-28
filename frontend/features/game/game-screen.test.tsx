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

import { MINIMUM_THINKING_MS } from './computer-thinking'
import { GameScreen } from './game-screen'
import {
  AFTER_E4,
  AFTER_E4_E5,
  COMPUTER_REPLIED_E5,
  computerGame,
  FOOLS_MATE,
  game,
  GAME_ID,
  MAIA_800,
  move,
  PROMOTION_GAME,
  START_FEN,
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

vi.mock('next/navigation', () => ({ notFound: vi.fn() }))
vi.mock('@/lib/page-load', () => ({ loadPage: vi.fn(), reloadPage: vi.fn() }))

const GAME_PATH = `/api/games/${GAME_ID}`
const MOVES_PATH = `${GAME_PATH}/moves`
const RESIGN_PATH = `${GAME_PATH}/resign`

// The position the board shows while the computer thinks about 1. e4.
const AFTER_E4_FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b - - 0 1'

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

  it('has no Hint control', async () => {
    answer({ [GAME_PATH]: ok(game()) })
    await renderGame()

    expect(screen.queryByText('Hint')).not.toBeInTheDocument()
  })

  it('highlights Play while the game is being played', async () => {
    answer({ [GAME_PATH]: ok(game()) })
    await renderGame()

    expect(
      screen.getByRole('status', { name: 'Highlighted' })
    ).toHaveTextContent('/play')
  })

  it('highlights Play while the game is still loading', () => {
    answer({ [GAME_PATH]: new Promise<Reply>(() => {}) })
    renderWithQueries(
      <PageNavItemProvider>
        <NamedNavItem />
        <GameScreen id={GAME_ID} />
      </PageNavItemProvider>
    )

    expect(
      screen.getByRole('status', { name: 'Highlighted' })
    ).toHaveTextContent('/play')
    expect(
      screen.queryByRole('img', { name: 'Chess board' })
    ).not.toBeInTheDocument()
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

  // The number comes from the game shown, not from counting the moves this
  // screen has sent: here one answer brings two moves.
  it('counts the half-move on from the game each answer brings', async () => {
    answer({ [GAME_PATH]: ok(game()), [MOVES_PATH]: ok(AFTER_E4_E5) })
    await renderGame()
    playOnBoard('e2', 'e4')
    await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4_E5.fen))

    const sent = answer({
      [GAME_PATH]: ok(AFTER_E4_E5),
      [MOVES_PATH]: ok(game()),
    })
    playOnBoard('g1', 'f3')

    await waitFor(() =>
      expect(sentMoves(sent)).toEqual([{ uci: 'g1f3', halfMoveNumber: 3 }])
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
      // Another tab played two moves meanwhile.
      answer({ [GAME_PATH]: ok(AFTER_E4_E5), [MOVES_PATH]: problem(409) })
      playOnBoard('g1', 'f3')
      await waitFor(() => expect(board.props!.fen).toBe(AFTER_E4_E5.fen))

      const sent = answer({
        [GAME_PATH]: ok(AFTER_E4_E5),
        [MOVES_PATH]: ok(game()),
      })
      playOnBoard('g1', 'f3')

      await waitFor(() =>
        expect(sentMoves(sent)).toEqual([{ uci: 'g1f3', halfMoveNumber: 3 }])
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
      expect(screen.getByRole('link', { name: 'New game' })).toHaveAttribute(
        'href',
        '/play'
      )
      expect(screen.queryByText('Your move')).not.toBeInTheDocument()
    })

    it('marks the mated king and lets nothing move', async () => {
      answer({ [GAME_PATH]: ok(FOOLS_MATE) })
      await renderGame()

      expect(board.props).toMatchObject({ check: 'e1', readOnly: true })
    })

    it('still highlights Play', async () => {
      answer({ [GAME_PATH]: ok(FOOLS_MATE) })
      await renderGame()

      expect(
        screen.getByRole('status', { name: 'Highlighted' })
      ).toHaveTextContent('/play')
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

  describe('a game against the computer', () => {
    // Lets `ms` pass on the fake clock, and React render what that changes.
    const pass = (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms))

    // The clock is faked only once the game has loaded, since the queries
    // that find it wait on real timers.
    const fakeTheClock = () =>
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })

    afterEach(() => {
      vi.useRealTimers()
    })

    const heading = () => screen.getByRole('heading', { level: 1 })

    it('names the computer by its strength, and the player "You"', async () => {
      answer({
        [GAME_PATH]: ok(
          computerGame({ opponent: { kind: 'computer', strength: 1400 } })
        ),
      })
      await renderGame()

      expect(screen.getByText('Maia 1400')).toBeInTheDocument()
      expect(screen.getAllByText('You')).toHaveLength(1)
      expect(heading()).toHaveTextContent('Your move')
      expect(screen.queryByText('Thinking…')).not.toBeInTheDocument()
    })

    it("opens a game as Black with the board turned and the computer's first move played", async () => {
      answer({
        [GAME_PATH]: ok({
          ...AFTER_E4,
          playerSide: 'black',
          opponent: MAIA_800,
        }),
      })
      await renderGame()

      expect(board.props).toMatchObject({
        fen: AFTER_E4.fen,
        orientation: 'black',
        lastMove: { from: 'e2', to: 'e4' },
        readOnly: false,
      })
      expect(heading()).toHaveTextContent('Your move')
      expect(screen.getByText('Black to move · move 1')).toBeInTheDocument()
      expect(screen.getByRole('cell', { name: 'e4' })).toHaveAttribute(
        'aria-current',
        'step'
      )
    })

    it('shows the computer thinking, and locks the board, from the moment the move is sent', async () => {
      answer({
        [GAME_PATH]: ok(computerGame()),
        [MOVES_PATH]: new Promise<Reply>(() => {}),
      })
      await renderGame()

      playOnBoard('e2', 'e4')

      await waitFor(() =>
        expect(heading()).toHaveTextContent('Maia 800 is thinking')
      )
      expect(screen.getByText('Black to move · move 1')).toBeInTheDocument()
      expect(screen.getByText('Thinking…').parentElement).toHaveTextContent(
        'Maia 800'
      )
      // The board shows the move, and nothing more can move.
      expect(board.props).toMatchObject({
        fen: AFTER_E4_FEN,
        lastMove: { from: 'e2', to: 'e4' },
        readOnly: true,
      })
      // The move list waits for the API to name the move.
      expect(screen.getByText('No moves yet.')).toBeInTheDocument()

      // Flipped, the computer's bar is the lower one, and says so.
      fireEvent.click(screen.getByRole('button', { name: 'Flip board' }))
      expect(screen.getByText('Thinking…').parentElement).toHaveTextContent(
        'Maia 800'
      )
    })

    it('holds the thinking state for the minimum time, then shows the reply', async () => {
      answer({
        [GAME_PATH]: ok(computerGame()),
        [MOVES_PATH]: ok(COMPUTER_REPLIED_E5),
      })
      await renderGame()
      fakeTheClock()

      playOnBoard('e2', 'e4')
      await pass(0)

      // The answer is in, with the reply, but the state holds: the list
      // shows the player's move and not the reply, and the board is locked.
      expect(heading()).toHaveTextContent('Maia 800 is thinking')
      expect(screen.getByRole('cell', { name: 'e4' })).toHaveAttribute(
        'aria-current',
        'step'
      )
      expect(screen.queryByRole('cell', { name: 'e5' })).toBeNull()
      expect(board.props).toMatchObject({
        fen: AFTER_E4_FEN,
        lastMove: { from: 'e2', to: 'e4' },
        readOnly: true,
      })

      await pass(MINIMUM_THINKING_MS - 1)
      expect(heading()).toHaveTextContent('Maia 800 is thinking')
      expect(board.props!.fen).toBe(AFTER_E4_FEN)

      await pass(1)
      expect(heading()).toHaveTextContent('Your move')
      expect(screen.getByText('White to move · move 2')).toBeInTheDocument()
      expect(screen.queryByText('Thinking…')).not.toBeInTheDocument()
      expect(screen.getByRole('cell', { name: 'e5' })).toHaveAttribute(
        'aria-current',
        'step'
      )
      expect(board.props).toMatchObject({
        fen: COMPUTER_REPLIED_E5.fen,
        lastMove: { from: 'e7', to: 'e5' },
        readOnly: false,
      })
      // The reply moved the pieces on the same board.
      expect(board.mounts).toBe(1)
    })

    it("marks the computer's king in check once the API has named the player's checking move", async () => {
      // 1. e4 d6, and the player gives check with 2. Bb5+.
      const beforeCheck = computerGame({
        fen: 'rnbqkbnr/ppp1pppp/3p4/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2',
        moves: [move(1, 'e2e4', 'e4'), move(2, 'd7d6', 'd6')],
        legalMoves: { f1: ['b5'] },
      })
      const replied = computerGame({
        fen: 'rnbqkbnr/pp2pppp/2pp4/1B6/4P3/8/PPPP1PPP/RNBQK1NR w KQkq - 0 3',
        moves: [
          ...beforeCheck.moves,
          move(3, 'f1b5', 'Bb5+'),
          move(4, 'c7c6', 'c6'),
        ],
      })
      let settle!: (reply: Reply) => void
      answer({
        [GAME_PATH]: ok(beforeCheck),
        [MOVES_PATH]: new Promise<Reply>(resolve => (settle = resolve)),
      })
      await renderGame()
      fakeTheClock()

      playOnBoard('f1', 'b5')
      await pass(0)
      // Until the API answers, nothing says the move gave check.
      expect(board.props).toMatchObject({
        fen: 'rnbqkbnr/ppp1pppp/3p4/1B6/4P3/8/PPPP1PPP/RNBQK1NR b - - 0 2',
        check: undefined,
      })

      settle(ok(replied))
      await pass(0)
      expect(heading()).toHaveTextContent('Maia 800 is thinking')
      expect(board.props!.check).toBe('e8')

      await pass(MINIMUM_THINKING_MS)
      expect(board.props).toMatchObject({ fen: replied.fen, check: undefined })
    })

    it("does not take another tab's move, read meanwhile, for the player's", async () => {
      answer({
        [GAME_PATH]: ok(computerGame()),
        [MOVES_PATH]: new Promise<Reply>(() => {}),
      })
      const { queryClient } = await renderGame()

      playOnBoard('g1', 'f3')
      await waitFor(() =>
        expect(heading()).toHaveTextContent('Maia 800 is thinking')
      )
      // Another tab played 1. e4 first, and this screen read the game again.
      act(() =>
        queryClient.setQueryData(['game', GAME_ID], {
          ...AFTER_E4,
          opponent: MAIA_800,
        })
      )
      // The screen has drawn the game read again.
      await waitFor(() =>
        expect(board.props!.destinations).toEqual(
          new Map([['e7', ['e6', 'e5']]])
        )
      )

      expect(heading()).toHaveTextContent('Maia 800 is thinking')
      expect(screen.queryByRole('cell', { name: 'e4' })).toBeNull()
      expect(screen.getByText('No moves yet.')).toBeInTheDocument()
    })

    it('shows a reply slower than the minimum time as soon as it comes', async () => {
      let settle!: (reply: Reply) => void
      answer({
        [GAME_PATH]: ok(computerGame()),
        [MOVES_PATH]: new Promise<Reply>(resolve => (settle = resolve)),
      })
      await renderGame()
      fakeTheClock()

      playOnBoard('e2', 'e4')
      await pass(MINIMUM_THINKING_MS + 200)
      expect(heading()).toHaveTextContent('Maia 800 is thinking')

      settle(ok(COMPUTER_REPLIED_E5))
      await pass(0)

      expect(heading()).toHaveTextContent('Your move')
      expect(board.props!.fen).toBe(COMPUTER_REPLIED_E5.fen)
    })

    it("shows the result at once, without the thinking state's hold, when the player's move ends the game", async () => {
      const mated = { ...FOOLS_MATE, playerSide: 'black', opponent: MAIA_800 }
      const beforeMate = computerGame({
        playerSide: 'black',
        fen: 'rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq - 0 2',
        sideToMove: 'black',
        moves: FOOLS_MATE.moves.slice(0, 3),
        legalMoves: { d8: ['h4'] },
      })
      answer({ [GAME_PATH]: ok(beforeMate), [MOVES_PATH]: ok(mated) })
      await renderGame()
      fakeTheClock()

      playOnBoard('d8', 'h4')
      await pass(0)

      expect(heading()).toHaveTextContent('You won')
      expect(screen.queryByText('Thinking…')).not.toBeInTheDocument()
      expect(board.props).toMatchObject({ fen: mated.fen, readOnly: true })
    })

    it('names the computer as the winner of a finished game', async () => {
      answer({
        [GAME_PATH]: ok({ ...FOOLS_MATE, opponent: MAIA_800 }),
      })
      await renderGame()

      expect(heading()).toHaveTextContent('Maia 800 won')
      expect(screen.getByText('Checkmate.')).toBeInTheDocument()
    })

    it('shows the result only once the hold is over when the reply ends the game', async () => {
      // Black's mating reply in Fool's mate, after 1. f3 e5 2. g4.
      const beforeMate = computerGame({
        fen: 'rnbqkbnr/pppp1ppp/8/4p3/8/5P2/PPPPP1PP/RNBQKBNR w KQkq - 0 2',
        moves: FOOLS_MATE.moves.slice(0, 2),
        legalMoves: { g2: ['g3', 'g4'] },
      })
      const mated = { ...FOOLS_MATE, opponent: MAIA_800 }
      answer({ [GAME_PATH]: ok(beforeMate), [MOVES_PATH]: ok(mated) })
      await renderGame()
      fakeTheClock()

      playOnBoard('g2', 'g4')
      await pass(0)

      expect(heading()).toHaveTextContent('Maia 800 is thinking')
      expect(screen.queryByText('Checkmate.')).not.toBeInTheDocument()
      expect(screen.queryByRole('cell', { name: 'Qh4#' })).toBeNull()

      await pass(MINIMUM_THINKING_MS)
      expect(heading()).toHaveTextContent('Maia 800 won')
      expect(screen.getByText('Checkmate.')).toBeInTheDocument()
      expect(board.props).toMatchObject({ fen: mated.fen, check: 'e1' })
    })

    it('shows the computer, as White, thinking about the next move when the player has Black', async () => {
      answer({
        [GAME_PATH]: ok({
          ...AFTER_E4,
          playerSide: 'black',
          opponent: MAIA_800,
        }),
        [MOVES_PATH]: new Promise<Reply>(() => {}),
      })
      await renderGame()

      playOnBoard('e7', 'e5')

      await waitFor(() =>
        expect(heading()).toHaveTextContent('Maia 800 is thinking')
      )
      expect(screen.getByText('White to move · move 2')).toBeInTheDocument()
      expect(screen.getByText('Thinking…').parentElement).toHaveTextContent(
        'Maia 800'
      )
    })

    it('shows the chosen piece on the last rank while the computer thinks about a promotion', async () => {
      answer({
        [GAME_PATH]: ok({ ...PROMOTION_GAME, opponent: MAIA_800 }),
        [MOVES_PATH]: new Promise<Reply>(() => {}),
      })
      await renderGame()

      playOnBoard('e7', 'e8')
      fireEvent.click(screen.getByRole('button', { name: 'Knight' }))

      await waitFor(() =>
        expect(heading()).toHaveTextContent('Maia 800 is thinking')
      )
      expect(board.props!.fen).toBe('4N3/6k1/8/8/8/8/8/4K3 b - - 0 1')
    })

    it('shows the move on a board mounted again while the computer thinks', async () => {
      answer({
        [GAME_PATH]: ok(computerGame()),
        [MOVES_PATH]: new Promise<Reply>(() => {}),
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
      await waitFor(() =>
        expect(heading()).toHaveTextContent('Maia 800 is thinking')
      )

      // Next.js hides the page and shows it again, which mounts the board
      // again: it is given the position with the move, not the one before.
      rerender(screenIn('hidden'))
      rerender(screenIn('visible'))

      await waitFor(() => expect(board.mounts).toBe(2))
      expect(heading()).toHaveTextContent('Maia 800 is thinking')
      expect(board.props).toMatchObject({
        fen: AFTER_E4_FEN,
        lastMove: { from: 'e2', to: 'e4' },
      })
    })

    it('ends the thinking state at once when the move fails, puts the board back and shows the error', async () => {
      // The query cache reports the failure to the console.
      const consoleError = vi
        .spyOn(console, 'error')
        .mockImplementation(() => {})
      answer({
        [GAME_PATH]: ok(computerGame()),
        [MOVES_PATH]: problem(503),
      })
      await renderGame()
      fakeTheClock()

      playOnBoard('e2', 'e4')
      await pass(0)

      expect(screen.getByRole('alert')).toHaveTextContent(
        'Your move was not sent'
      )
      expect(heading()).toHaveTextContent('Your move')
      expect(screen.queryByText('Thinking…')).not.toBeInTheDocument()
      expect(board.mounts).toBe(2)
      expect(board.props).toMatchObject({ fen: START_FEN, readOnly: false })
      consoleError.mockRestore()
    })
  })

  describe('resigning', () => {
    const QUESTION = 'Resign this game? This cannot be undone.'

    // The game the API answers a resignation with: the player had White
    // against the computer, so the computer has won.
    const RESIGNED = computerGame({
      ...AFTER_E4_E5,
      opponent: MAIA_800,
      status: 'finished',
      result: 'blackWins',
      endReason: 'resignation',
      legalMoves: {},
    })

    const confirmation = () => screen.getByRole('group', { name: QUESTION })
    const resignControl = () => screen.getByRole('button', { name: 'Resign' })
    const resignations = (sent: ReturnType<typeof answer>) =>
      sent.filter(request => request.call === `POST ${RESIGN_PATH}`)

    async function openConfirmation(reply?: Reply | Promise<Reply>) {
      const sent = answer({
        [GAME_PATH]: ok(computerGame(AFTER_E4_E5)),
        ...(reply && { [RESIGN_PATH]: reply }),
      })
      await renderGame()
      fireEvent.click(resignControl())
      return sent
    }

    it('shows Resign beside Flip board, outside the confirmation, while the game is played', async () => {
      answer({ [GAME_PATH]: ok(computerGame()) })
      await renderGame()

      expect(resignControl()).not.toHaveAttribute('aria-disabled', 'true')
      expect(
        screen.getByRole('button', { name: 'Flip board' })
      ).toBeInTheDocument()
      expect(screen.queryByRole('group', { name: QUESTION })).toBeNull()
    })

    it('replaces the controls with the confirmation, Cancel first, and moves focus into it', async () => {
      const sent = await openConfirmation()

      const group = confirmation()
      expect(group).toHaveTextContent(QUESTION)
      expect(
        within(group)
          .getAllByRole('button')
          .map(button => button.textContent)
      ).toEqual(['Cancel', 'Resign'])
      expect(
        within(group).getByRole('button', { name: 'Cancel' })
      ).toHaveFocus()
      // The controls are gone: the only Resign left is the confirmation's.
      expect(screen.queryByRole('button', { name: 'Flip board' })).toBeNull()
      expect(screen.getAllByRole('button', { name: 'Resign' })).toHaveLength(1)
      expect(resignations(sent)).toEqual([])
    })

    it('puts the controls back on Cancel, sends nothing, and returns focus to Resign', async () => {
      const sent = await openConfirmation()

      fireEvent.click(
        within(confirmation()).getByRole('button', { name: 'Cancel' })
      )

      expect(screen.queryByRole('group', { name: QUESTION })).toBeNull()
      expect(resignControl()).toHaveFocus()
      expect(
        screen.getByRole('button', { name: 'Flip board' })
      ).toBeInTheDocument()
      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
        'Your move'
      )
      expect(resignations(sent)).toEqual([])
    })

    it('sends the resignation once however often it is pressed, then shows the result', async () => {
      let settle!: (reply: Reply) => void
      const sent = await openConfirmation(
        new Promise<Reply>(resolve => (settle = resolve))
      )
      const confirm = within(confirmation()).getByRole('button', {
        name: 'Resign',
      })

      fireEvent.click(confirm)
      fireEvent.click(confirm)
      fireEvent.click(confirm)
      await waitFor(() =>
        expect(confirm).toHaveAttribute('aria-disabled', 'true')
      )
      // Nor can it be cancelled once it has gone.
      fireEvent.click(
        within(confirmation()).getByRole('button', { name: 'Cancel' })
      )
      expect(confirmation()).toBeInTheDocument()

      settle(ok(RESIGNED))

      await waitFor(() =>
        expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
          'Maia 800 won'
        )
      )
      expect(screen.getByText('You resigned.')).toBeInTheDocument()
      expect(screen.getByRole('link', { name: 'New game' })).toHaveAttribute(
        'href',
        '/play'
      )
      expect(screen.queryByRole('group', { name: QUESTION })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Resign' })).toBeNull()
      expect(
        screen.getByRole('button', { name: 'Flip board' })
      ).toBeInTheDocument()
      expect(board.props!.readOnly).toBe(true)
      expect(resignations(sent)).toHaveLength(1)
    })

    describe('that fails', () => {
      // The query cache reports each failure to the console.
      beforeEach(() => {
        vi.spyOn(console, 'error').mockImplementation(() => {})
      })
      afterEach(() => {
        vi.mocked(console.error).mockRestore()
      })

      it.each([
        ['fails', () => problem(500)],
        ['cannot be reached', unreachable],
      ])(
        'keeps the confirmation open, with no message, when the API %s, and sends it again on Resign',
        async (_, reply) => {
          const sent = await openConfirmation(reply())

          fireEvent.click(
            within(confirmation()).getByRole('button', { name: 'Resign' })
          )

          await waitFor(() => expect(resignations(sent)).toHaveLength(1))
          const confirm = within(confirmation()).getByRole('button', {
            name: 'Resign',
          })
          await waitFor(() =>
            expect(confirm).not.toHaveAttribute('aria-disabled', 'true')
          )
          expect(screen.queryByRole('alert')).toBeNull()
          expect(confirmation()).toHaveTextContent(QUESTION)
          expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
            'Your move'
          )

          const retried = answer({
            [GAME_PATH]: ok(RESIGNED),
            [RESIGN_PATH]: ok(RESIGNED),
          })
          fireEvent.click(confirm)

          await waitFor(() =>
            expect(screen.getByText('You resigned.')).toBeInTheDocument()
          )
          expect(resignations(retried)).toHaveLength(1)
        }
      )
    })

    it('loads the game and shows its result when the API finds it already over', async () => {
      await openConfirmation(problem(409))
      // The game was resigned in another tab.
      const sent = answer({
        [GAME_PATH]: ok(RESIGNED),
        [RESIGN_PATH]: problem(409),
      })

      fireEvent.click(
        within(confirmation()).getByRole('button', { name: 'Resign' })
      )

      await waitFor(() =>
        expect(screen.getByText('You resigned.')).toBeInTheDocument()
      )
      expect(calls(sent)).toEqual([`POST ${RESIGN_PATH}`, `GET ${GAME_PATH}`])
      expect(screen.queryByRole('group', { name: QUESTION })).toBeNull()
      expect(screen.queryByRole('alert')).toBeNull()
    })

    it('cannot be used while a move is being sent and the computer thinks', async () => {
      let settle!: (reply: Reply) => void
      const sent = answer({
        [GAME_PATH]: ok(computerGame()),
        [MOVES_PATH]: new Promise<Reply>(resolve => (settle = resolve)),
      })
      await renderGame()

      playOnBoard('e2', 'e4')
      await waitFor(() =>
        expect(resignControl()).toHaveAttribute('aria-disabled', 'true')
      )
      fireEvent.click(resignControl())

      expect(screen.queryByRole('group', { name: QUESTION })).toBeNull()
      expect(resignations(sent)).toEqual([])

      settle(ok(COMPUTER_REPLIED_E5))
      await waitFor(
        () =>
          expect(resignControl()).not.toHaveAttribute('aria-disabled', 'true'),
        { timeout: MINIMUM_THINKING_MS + 1000 }
      )
    })

    it("refuses the confirmation's Resign while a move made meanwhile is being sent", async () => {
      const sent = answer({
        [GAME_PATH]: ok(game()),
        [MOVES_PATH]: new Promise<Reply>(() => {}),
        [RESIGN_PATH]: ok(RESIGNED),
      })
      await renderGame()
      fireEvent.click(resignControl())

      playOnBoard('e2', 'e4')
      const confirm = within(confirmation()).getByRole('button', {
        name: 'Resign',
      })
      await waitFor(() =>
        expect(confirm).toHaveAttribute('aria-disabled', 'true')
      )
      fireEvent.click(confirm)

      expect(resignations(sent)).toEqual([])
    })

    it('shows the result of a resigned game, with no Resign control', async () => {
      answer({ [GAME_PATH]: ok(RESIGNED) })
      await renderGame()

      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
        'Maia 800 won'
      )
      expect(screen.getByText('You resigned.')).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Resign' })).toBeNull()
      expect(
        screen.getByRole('button', { name: 'Flip board' })
      ).toBeInTheDocument()
    })
  })

  it('shows no thinking state in a game where the player moves both sides', async () => {
    answer({
      [GAME_PATH]: ok(game()),
      [MOVES_PATH]: new Promise<Reply>(() => {}),
    })
    await renderGame()

    playOnBoard('e2', 'e4')

    await waitFor(() => expect(board.props!.readOnly).toBe(true))
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Your move'
    )
    expect(screen.queryByText('Thinking…')).not.toBeInTheDocument()
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
