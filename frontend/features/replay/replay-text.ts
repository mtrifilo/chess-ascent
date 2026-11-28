import { opposite } from '@lichess-org/chessground/util'

import type { Side } from '@/components/chess-board/chess-board'
import {
  outcomeFor,
  playerName,
  playsComputer,
  type Game,
} from '@/features/game/game-state'
import { dateText, END_REASON_IN_LIST } from '@/features/my-games/game-row-text'
import type { components } from '@/types/api'

type Result = components['schemas']['GameResult']
type EndReason = components['schemas']['GameEndReason']

// The line under the Replay heading, such as "You won by checkmate against
// Maia 800 · 25 Nov 2026": how the game ended for the player, why, against
// whom, and the day of its last activity (its last move, or the
// resignation) in the viewer's own time zone. A game in which the player
// moves both sides has no "against". The API sends a result and a reason
// with every finished game.
export function replaySummary(game: Game): string {
  const { result, endReason } = game
  if (!result || !endReason) return ''
  const against = playsComputer(game)
    ? ` against ${playerName(game, opposite(game.playerSide))}`
    : ''
  const ending = howItEnded(game.playerSide, result, endReason, against)
  return `${ending} · ${dateText(game.lastActivityAt)}`
}

// Only the player resigns, so a resignation is always theirs.
function howItEnded(
  playerSide: Side,
  result: Result,
  endReason: EndReason,
  against: string
): string {
  if (endReason === 'resignation') return `You resigned${against}`
  if (endReason === 'deadPosition') {
    return `Draw${against}, ${END_REASON_IN_LIST.deadPosition}`
  }
  const outcome = OUTCOME[outcomeFor(playerSide, result)]
  return `${outcome} by ${END_REASON_IN_LIST[endReason]}${against}`
}

const OUTCOME = { won: 'You won', lost: 'You lost', draw: 'Draw' } as const
