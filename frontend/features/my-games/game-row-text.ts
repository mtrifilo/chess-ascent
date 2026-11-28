import { opposite } from '@lichess-org/chessground/util'

import { playerName, sideLabel } from '@/features/game/game-state'
import type { components } from '@/types/api'

// Every word a row of My games shows, built from the facts the API sends
// about a game; the API sends no wording.

export type GameSummary = components['schemas']['GameSummaryResponse']
type Result = components['schemas']['GameResult']
type EndReason = components['schemas']['GameEndReason']
type Side = GameSummary['playerSide']

// What a row shows, in the order the desktop table's columns show it.
export type GameRowText = {
  opponent: string
  colour: string
  result: string
  date: string
}

export function gameRowText(game: GameSummary): GameRowText {
  return {
    opponent: playerName(game, opposite(game.playerSide)),
    colour: sideLabel(game.playerSide),
    result: resultText(game),
    date: dateText(game.lastActivityAt),
  }
}

// A finished game's reason for ending, as the second half of its result,
// such as "Won, checkmate". Each follows the reason the game screen gives
// when the game ends.
export const END_REASON_IN_LIST: Record<EndReason, string> = {
  checkmate: 'checkmate',
  stalemate: 'stalemate',
  threefoldRepetition: 'threefold repetition',
  fiftyMoveRule: 'fifty-move rule',
  deadPosition: 'neither side can checkmate',
  resignation: 'resigned',
}

// An unfinished game reads "In progress, move 6"; a finished one reads how
// it ended for the player, then why, such as "Lost, resigned". The API sends
// a result and a reason with every finished game.
export function resultText(game: GameSummary): string {
  if (game.status === 'ongoing') {
    return `In progress, move ${moveNumber(game.halfMoveCount)}`
  }
  if (!game.result || !game.endReason) return ''
  return `${outcome(game.playerSide, game.result)}, ${END_REASON_IN_LIST[game.endReason]}`
}

// The move a game is on, counted as the game screen counts it: a move is a
// move by White and Black's reply, so after 10 or 11 half-moves the game is
// on move 6.
function moveNumber(halfMoveCount: number): number {
  return Math.floor(halfMoveCount / 2) + 1
}

function outcome(playerSide: Side, result: Result): 'Won' | 'Lost' | 'Draw' {
  if (result === 'draw') return 'Draw'
  const winner: Side = result === 'whiteWins' ? 'white' : 'black'
  return winner === playerSide ? 'Won' : 'Lost'
}

// The day of an ISO 8601 time, such as "26 Nov 2026", in the viewer's own
// time zone, or in `timeZone` when one is given. The parts are put in this
// order here rather than taken from a locale's pattern, so that every month
// is three letters: British English, whose order this is, writes September
// as "Sept".
export function dateText(isoTime: string, timeZone?: string): string {
  const parts = new Intl.DateTimeFormat('en-US', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone,
  }).formatToParts(new Date(isoTime))
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find(candidate => candidate.type === type)?.value
  return `${part('day')} ${part('month')} ${part('year')}`
}
