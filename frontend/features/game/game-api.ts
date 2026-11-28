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
// and records it, so the game's page opens without asking for it again.
export function useCreateGame() {
  const queryClient = useQueryClient()
  return useMutation({
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
export function usePlayMove(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (uci: string): Promise<MoveOutcome> => {
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
