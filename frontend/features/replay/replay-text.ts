import { opposite } from '@lichess-org/chessground/util'

import type { Side } from '@/components/chess-board/chess-board'
import {
  playerName,
  playsComputer,
  winner,
  type Game,
} from '@/features/game/game-state'
import { dateText } from '@/features/my-games/game-row-text'
import type { components } from '@/types/api'

type Result = components['schemas']['GameResult']
type EndReason = components['schemas']['GameEndReason']

// The line under the Replay heading, such as "You won by checkmate against
// Maia 800 · 25 Nov 2026": how the game ended for the player, why, against
// whom, and the day of its last move in the viewer's own time zone. A game
// in which the player moves both sides has no "against". The API sends a
// result and a reason with every finished game.
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
    return `Draw${against}, neither side can checkmate`
  }
  return `${outcome(playerSide, result)} by ${BY_REASON[endReason]}${against}`
}

const BY_REASON: Record<
  Exclude<EndReason, 'resignation' | 'deadPosition'>,
  string
> = {
  checkmate: 'checkmate',
  stalemate: 'stalemate',
  threefoldRepetition: 'threefold repetition',
  fiftyMoveRule: 'fifty-move rule',
}

function outcome(playerSide: Side, result: Result): string {
  const side = winner(result)
  if (!side) return 'Draw'
  return side === playerSide ? 'You won' : 'You lost'
}
