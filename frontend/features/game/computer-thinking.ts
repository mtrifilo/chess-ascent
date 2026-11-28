'use client'

import { useEffect, useState } from 'react'

import type { usePlayMove } from './game-api'
import { playsComputer, type Game } from './game-state'

// The shortest time the screen shows the computer thinking. One request
// plays the player's move and brings the computer's reply, usually within
// tens of milliseconds, and a reply shown that soon reads as a flash. The
// value is a placeholder until the owner settles it.
export const MINIMUM_THINKING_MS = 500

// What the screen shows while the computer is thinking: the position the
// player's move was made in, which the board shows with that move on it,
// and the moves up to and including the player's. Until the API answers,
// that move is not among them, since only the API names it in notation.
export type Thinking = { fen: string; moves: Game['moves'] }

// Whether the computer is thinking, and if so what to show. In a game
// against the computer it is from the moment the player's move is sent
// until the reply is shown: while the request is on its way, and after it
// brings a reply, until MINIMUM_THINKING_MS have passed since the move was
// sent. A request that fails ends it at once, and so does an answer with no
// reply, because the player's move ended the game: the API alone can tell
// that, so the screen shows the thinking state until its answer comes.
export function useComputerThinking(
  game: Game,
  playMove: ReturnType<typeof usePlayMove>
): Thinking | undefined {
  const holding = useStillWithin(playMove.submittedAt, MINIMUM_THINKING_MS)
  const sent = playMove.variables
  if (!playsComputer(game) || !sent) return undefined

  const replied =
    playMove.isSuccess &&
    playMove.data === 'played' &&
    game.moves.length > sent.halfMoveNumber
  if (!playMove.isPending && !(replied && holding)) return undefined

  return { fen: sent.fen, moves: game.moves.slice(0, sent.halfMoveNumber) }
}

// Whether fewer than `ms` milliseconds have passed since `since`, a time
// from Date.now(), where 0 is never. The answer changes with time, so a
// timer renders the caller again when it does.
function useStillWithin(since: number, ms: number): boolean {
  // The latest `since` whose time is up.
  const [over, setOver] = useState(0)
  useEffect(() => {
    if (since === 0) return
    const timer = setTimeout(() => setOver(since), since + ms - Date.now())
    return () => clearTimeout(timer)
  }, [since, ms])
  return since !== 0 && over !== since
}
