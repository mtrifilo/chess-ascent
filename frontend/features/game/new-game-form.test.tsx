import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { useRouter } from 'next/navigation'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { answer, type Reply, renderWithQueries } from '@/lib/test-support'

import { NewGameForm } from './new-game-form'
import { computerGame, GAME_ID } from './test-games'

vi.mock('next/navigation', () => ({ useRouter: vi.fn() }))

const push = vi.fn()

beforeEach(() => {
  push.mockReset()
  vi.mocked(useRouter).mockReturnValue({ push } as unknown as ReturnType<
    typeof useRouter
  >)
})

const created = (body = computerGame()): Reply => ({ status: 201, body })

const group = (name: string) => screen.getByRole('group', { name })
const choice = (groupName: string, option: string) =>
  within(group(groupName)).getByRole('radio', { name: option })
const startGame = () =>
  fireEvent.click(screen.getByRole('button', { name: 'Start game' }))

describe('NewGameForm', () => {
  it('offers the sides and the strengths, with White and 800 chosen', () => {
    renderWithQueries(<NewGameForm />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'New game'
    )
    const sides = within(group('Play as')).getAllByRole('radio')
    expect(sides.map(radio => radio.parentElement?.textContent)).toEqual([
      'White',
      'Black',
    ])
    expect(choice('Play as', 'White')).toBeChecked()

    const strengths = within(group('Opponent strength')).getAllByRole('radio')
    expect(strengths.map(radio => radio.parentElement?.textContent)).toEqual([
      '600',
      '800',
      '1000',
      '1200',
      '1400',
    ])
    expect(choice('Opponent strength', '800')).toBeChecked()
    expect(group('Opponent strength')).toHaveAccessibleDescription(
      'Higher numbers are stronger opponents.'
    )
  })

  it('makes each group one choice', () => {
    renderWithQueries(<NewGameForm />)

    fireEvent.click(choice('Play as', 'Black'))
    fireEvent.click(choice('Opponent strength', '1400'))

    expect(choice('Play as', 'Black')).toBeChecked()
    expect(choice('Play as', 'White')).not.toBeChecked()
    expect(choice('Opponent strength', '1400')).toBeChecked()
    expect(choice('Opponent strength', '800')).not.toBeChecked()
  })

  it('starts a game with the choices shown, and opens it', async () => {
    const sent = answer({ '/api/games': created() })
    const { queryClient } = renderWithQueries(<NewGameForm />)

    startGame()

    await waitFor(() => expect(push).toHaveBeenCalledWith(`/games/${GAME_ID}`))
    expect(sent).toEqual([
      {
        call: 'POST /api/games',
        body: {
          playerSide: 'white',
          opponent: { kind: 'computer', strength: 800 },
        },
      },
    ])
    // The game's page opens on the game already read.
    expect(queryClient.getQueryData(['game', GAME_ID])).toEqual(computerGame())
  })

  it('sends the side and the strength chosen', async () => {
    const sent = answer({
      '/api/games': created(computerGame({ playerSide: 'black' })),
    })
    renderWithQueries(<NewGameForm />)
    fireEvent.click(choice('Play as', 'Black'))
    fireEvent.click(choice('Opponent strength', '1400'))

    startGame()

    await waitFor(() => expect(push).toHaveBeenCalledOnce())
    expect(sent.map(request => request.body)).toEqual([
      { playerSide: 'black', opponent: { kind: 'computer', strength: 1400 } },
    ])
  })

  it('starts one game for a double press', async () => {
    let settle!: (reply: Reply) => void
    const sent = answer({
      '/api/games': new Promise<Reply>(resolve => (settle = resolve)),
    })
    renderWithQueries(<NewGameForm />)
    const button = screen.getByRole('button', { name: 'Start game' })

    fireEvent.click(button)
    await waitFor(() => expect(button).toBeDisabled())
    // The choices cannot change from those sent.
    expect(group('Play as')).toBeDisabled()
    expect(group('Opponent strength')).toBeDisabled()
    fireEvent.click(button)
    settle(created())

    await waitFor(() => expect(push).toHaveBeenCalledOnce())
    expect(sent).toHaveLength(1)
  })

  describe('when the API does not start a game', () => {
    // The query cache reports each failure to the console.
    beforeEach(() => {
      vi.spyOn(console, 'error').mockImplementation(() => {})
    })
    afterEach(() => {
      vi.mocked(console.error).mockRestore()
    })

    it('stays on the form, with the choices kept, and reports why', async () => {
      answer({ '/api/games': { status: 503, body: { status: 503 } } })
      renderWithQueries(<NewGameForm />)
      fireEvent.click(choice('Play as', 'Black'))
      fireEvent.click(choice('Opponent strength', '1200'))

      startGame()

      await waitFor(() =>
        expect(console.error).toHaveBeenCalledWith(
          new Error('POST /games answered 503.')
        )
      )
      expect(push).not.toHaveBeenCalled()
      expect(choice('Play as', 'Black')).toBeChecked()
      expect(choice('Opponent strength', '1200')).toBeChecked()
      expect(screen.getByRole('button', { name: 'Start game' })).toBeEnabled()
      expect(group('Play as')).toBeEnabled()
      // The designs have no words for this failure, so none are shown.
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })
  })
})
