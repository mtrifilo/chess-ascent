import { fireEvent, screen, waitFor } from '@testing-library/react'
import { useRouter } from 'next/navigation'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { answer, type Reply, renderWithQueries } from '@/lib/test-support'

import { NewGameButton } from './new-game-button'
import { game, GAME_ID } from './test-games'

vi.mock('next/navigation', () => ({ useRouter: vi.fn() }))

const push = vi.fn()

beforeEach(() => {
  push.mockReset()
  vi.mocked(useRouter).mockReturnValue({ push } as unknown as ReturnType<
    typeof useRouter
  >)
})

describe('NewGameButton', () => {
  it('starts a game in which the player moves both sides, and opens it', async () => {
    const sent = answer({ '/api/games': { status: 201, body: game() } })
    const { queryClient } = renderWithQueries(<NewGameButton />)

    fireEvent.click(screen.getByRole('button', { name: 'New game' }))

    await waitFor(() => expect(push).toHaveBeenCalledWith(`/games/${GAME_ID}`))
    expect(sent).toEqual([
      {
        call: 'POST /api/games',
        body: { playerSide: 'white', opponent: { kind: 'none' } },
      },
    ])
    // The game's page opens on the game already read.
    expect(queryClient.getQueryData(['game', GAME_ID])).toEqual(game())
  })

  it('starts one game for a double click', async () => {
    let settle!: (reply: Reply) => void
    const sent = answer({
      '/api/games': new Promise<Reply>(resolve => (settle = resolve)),
    })
    renderWithQueries(<NewGameButton />)
    const button = screen.getByRole('button', { name: 'New game' })

    fireEvent.click(button)
    await waitFor(() => expect(button).toBeDisabled())
    fireEvent.click(button)
    settle({ status: 201, body: game() })

    await waitFor(() => expect(push).toHaveBeenCalledOnce())
    expect(sent).toHaveLength(1)
  })

  it('stays put, and reports why, when the API does not start a game', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    answer({ '/api/games': { status: 500, body: { status: 500 } } })
    renderWithQueries(<NewGameButton />)

    fireEvent.click(screen.getByRole('button', { name: 'New game' }))

    await waitFor(() =>
      expect(consoleError).toHaveBeenCalledWith(
        new Error('POST /games answered 500.')
      )
    )
    expect(push).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'New game' })).toBeEnabled()
    consoleError.mockRestore()
  })
})
