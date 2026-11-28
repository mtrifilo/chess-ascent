'use client'

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api } from '@/lib/api'

import type { Side } from '@/components/chess-board/chess-board'

import type { Game, OpponentStrength } from './game-state'

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

// The choices a game against the computer starts with.
export type NewGame = { playerSide: Side; strength: OpponentStrength }

// Starts a game against the computer and records it, so the game's page
// shows it at once rather than waiting for its own read of the game. When
// the player has Black, the API's answer already holds the computer's first
// move.
export function useCreateGame() {
  const queryClient = useQueryClient()
  return useMutation({
    networkMode: 'always',
    mutationFn: async ({ playerSide, strength }: NewGame): Promise<Game> => {
      const { data, response } = await api.POST('/games', {
        body: { playerSide, opponent: { kind: 'computer', strength } },
      })
      if (!data) throw new Error(`POST /games answered ${response.status}.`)
      queryClient.setQueryData(gameKey(data.id), data)
      return data
    },
  })
}

// How a move ended, besides failing, which is thrown: `played`, and the
// recorded game is the API's answer; `reloaded`, because the game was no
// longer where this screen thought (409: it had ended, or its next half-move
// was not the one the move was sent as, as when another tab or request moved
// first; a wrong number from this screen would look the same), so it has
// been read again; `signed-out`, which the API client
// already handles by sending the visitor to sign in. Every other answer is a
// failure, a 503 included (a computer opponent that did not reply): nothing
// was saved, and the screen offers the same move again.
export type MoveOutcome = 'played' | 'reloaded' | 'signed-out'

// Sends a move in UCI form. This is the one place a move is posted.
//
// A game read that was already on its way when the move was played is
// cancelled before the answer is recorded, so it cannot land afterwards and
// put back the position before the move. The answer is recorded before the
// move counts as played: useComputerThinking (computer-thinking.ts) reads
// the computer's reply from the recorded game as soon as it does.
//
// A move that fails is followed by a fresh read of the game, since a move
// whose answer was lost may have been saved all the same. The read is not
// waited for: the failure is reported at once.
//
// The mutations here are sent even when the browser reports itself offline
// (`networkMode: 'always'`), so that a move fails at once, as the screen
// describes, rather than waiting unseen for the connection to come back.
//
// A move is sent with the half-move number the screen expects it to be, so
// the API refuses it (409) rather than playing it in a position the screen
// has not shown. It also carries the position it was made in (`fen`), which
// is not sent: that stays with the mutation's state, so the screen can tell
// whether a failure is about the position it now shows, even after the page
// was hidden while the move was on its way.
export type MoveRequest = { uci: string; halfMoveNumber: number; fen: string }

export function usePlayMove(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    networkMode: 'always',
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: gameKey(id) })
    },
    mutationFn: async ({
      uci,
      halfMoveNumber,
    }: MoveRequest): Promise<MoveOutcome> => {
      const { data, response } = await api.POST('/games/{id}/moves', {
        params: { path: { id } },
        body: { uci, halfMoveNumber },
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

// How a resignation ended, besides failing, which is thrown: `resigned`, and
// the recorded game is the API's answer; `reloaded`, because the game was
// already over (409: it ended by another move, or was resigned in another
// tab or by an earlier press whose answer was lost), so it has been read
// again; `signed-out`, which the API client already handles by sending the
// visitor to sign in. Every other answer is a failure, after which the
// player may send it again: resigning twice is safe, since the second
// answer is the 409.
export type ResignOutcome = 'resigned' | 'reloaded' | 'signed-out'

// Resigns the game. A game read already on its way is cancelled before the
// answer is recorded, as for a move, so it cannot put back the game as it
// stood before.
export function useResign(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    networkMode: 'always',
    mutationFn: async (): Promise<ResignOutcome> => {
      const { data, response } = await api.POST('/games/{id}/resign', {
        params: { path: { id } },
      })
      if (data) {
        await queryClient.cancelQueries({ queryKey: gameKey(id) })
        queryClient.setQueryData(gameKey(id), data)
        return 'resigned'
      }
      if (response.status === 409) {
        await queryClient.invalidateQueries({ queryKey: gameKey(id) })
        return 'reloaded'
      }
      if (response.status === 401) return 'signed-out'
      throw new Error(`POST /games/{id}/resign answered ${response.status}.`)
    },
  })
}
