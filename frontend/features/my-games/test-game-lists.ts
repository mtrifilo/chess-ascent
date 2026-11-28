import { MAIA_800 } from '@/features/game/test-games'
import type { components } from '@/types/api'

import type { GameSummary } from './game-row-text'

// Games and pages of them as the list API answers them, for tests. Imported
// only by tests.

type GameListResponse = components['schemas']['GameListResponse']

let nextId = 1

// A fresh id for each game, so that rows have keys of their own.
function gameId(): string {
  const suffix = String(nextId++).padStart(12, '0')
  return `00000000-0000-4000-8000-${suffix}`
}

// A game against the computer at strength 800, the player as White, five
// moves in; `overrides` replaces any field.
export function unfinishedGame(
  overrides: Partial<GameSummary> = {}
): GameSummary {
  return {
    id: gameId(),
    playerSide: 'white',
    opponent: MAIA_800,
    status: 'ongoing',
    result: null,
    endReason: null,
    halfMoveCount: 10,
    lastActivityAt: '2026-11-26T15:00:00Z',
    ...overrides,
  }
}

// A game the player won by checkmate; `overrides` replaces any field.
export function finishedGame(
  overrides: Partial<GameSummary> = {}
): GameSummary {
  return unfinishedGame({
    status: 'finished',
    result: 'whiteWins',
    endReason: 'checkmate',
    halfMoveCount: 37,
    lastActivityAt: '2026-11-25T15:00:00Z',
    ...overrides,
  })
}

export function page(
  games: GameSummary[],
  nextCursor: string | null = null
): GameListResponse {
  return { games, nextCursor }
}
