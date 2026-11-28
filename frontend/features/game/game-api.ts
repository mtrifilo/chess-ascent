'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api } from '@/lib/api'

import type { Game } from './game-state'

const gameKey = (id: string) => ['game', id] as const

// One of the signed-in player's games, or null when they have none with this
// id: the API answers 404 alike for a game that does not exist and one that
// belongs to someone else. Any other failure is thrown.
export function useGame(id: string) {
  return useQuery({
    queryKey: gameKey(id),
    queryFn: async ({ signal }): Promise<Game | null> => {
      const { data, response } = await api.GET('/games/{id}', {
        params: { path: { id } },
        signal,
      })
      if (data) return data
      if (response.status === 404) return null
      throw new Error(`GET /games/{id} answered ${response.status}.`)
    },
  })
}

// Starts a game in which the signed-in player moves both sides, as White,
// and records it, so the game's page shows it at once rather than waiting
// for its own read of the game.
export function useCreateGame() {
  const queryClient = useQueryClient()
  return useMutation({
    networkMode: 'always',
    mutationFn: async (): Promise<Game> => {
      const { data, response } = await api.POST('/games', {
        body: { playerSide: 'white', opponent: { kind: 'none' } },
      })
      if (!data) throw new Error(`POST /games answered ${response.status}.`)
      queryClient.setQueryData(gameKey(data.id), data)
      return data
    },
  })
}

// How a move ended, besides failing, which is thrown: `played`, and the
// recorded game is the API's answer; `reloaded`, because the game was no
// longer where this screen thought (409: it had ended, or another request
// moved first), so it has been read again; `signed-out`, which the API
// client already handles by sending the visitor to sign in.
export type MoveOutcome = 'played' | 'reloaded' | 'signed-out'

// Sends a move in UCI form. This is the one place a move is posted.
//
// A game read that was already on its way when the move was played is
// cancelled before the answer is recorded, so it cannot land afterwards and
// put back the position before the move.
//
// A move that fails is followed by a fresh read of the game. The screen
// offers only moves the API listed, so a refusal means the screen's position
// is out of date (another tab has moved), and a move whose answer was lost
// may have been saved all the same. The read is not waited for: the failure
// is reported at once.
//
// The mutations here are sent even when the browser reports itself offline
// (`networkMode: 'always'`), so that a move fails at once, as the screen
// describes, rather than waiting unseen for the connection to come back.
//
// A move carries the position it was made in (`fen`), which is not sent: it
// stays with the mutation's state, so the screen can tell whether a failure
// is about the position it now shows, even after the page was hidden while
// the move was on its way.
export type MoveRequest = { uci: string; fen: string }

export function usePlayMove(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    networkMode: 'always',
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: gameKey(id) })
    },
    mutationFn: async ({ uci }: MoveRequest): Promise<MoveOutcome> => {
      const { data, response } = await api.POST('/games/{id}/moves', {
        params: { path: { id } },
        body: { uci },
      })
      if (data) {
        await queryClient.cancelQueries({ queryKey: gameKey(id) })
        queryClient.setQueryData(gameKey(id), data)
        return 'played'
      }
      if (response.status === 409) {
        await queryClient.invalidateQueries({ queryKey: gameKey(id) })
        return 'reloaded'
      }
      if (response.status === 401) return 'signed-out'
      throw new Error(`POST /games/{id}/moves answered ${response.status}.`)
    },
  })
}
