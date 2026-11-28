'use client'

import { useEffect, useState } from 'react'

import type { BoardMove, Square } from '@/components/chess-board/chess-board'

import type { usePlayMove } from './game-api'
import {
  checkSquare,
  playsComputer,
  positionAfter,
  squaresOf,
  type Game,
} from './game-state'

// The shortest time the screen shows the computer thinking. One request
// plays the player's move and brings the computer's reply, usually within
// tens of milliseconds, and a reply shown that soon reads as a flash. The
// value is a placeholder until the owner settles it.
export const MINIMUM_THINKING_MS = 500

// What the screen shows while the computer is thinking: the position after
// the player's move, with that move marked, and the moves up to and
// including the player's. Until the API answers, that move is not among
// them, since only the API names it in notation, and so nothing is marked
// as in check either.
export type Thinking = {
  fen: string
  moves: Game['moves']
  lastMove: BoardMove
  check: Square | undefined
}

// Whether the computer is thinking, and if so what to show. In a game
// against the computer it is from the moment the player's move is sent
// until the reply is shown: while the request is on its way, and after it
// brings a reply, until MINIMUM_THINKING_MS have passed since the move was
// sent. A request that fails ends it at once, and so does an answer with no
// reply, because the player's move ended the game: the API alone can tell
// that, so the screen shows the thinking state until its answer comes.
//
// The reply is read from `game`, the recorded game, which usePlayMove
// replaces with the API's answer before the move counts as played.
export function useComputerThinking(
  game: Game,
  playMove: ReturnType<typeof usePlayMove>
): Thinking | undefined {
  const computer = playsComputer(game)
  const holding = useStillWithin(
    computer ? playMove.submittedAt : 0,
    MINIMUM_THINKING_MS
  )
  const sent = playMove.variables
  if (!computer || !sent) return undefined

  // Half-moves count from 1, so the player's move is the `halfMoveNumber`th
  // and a reply makes the game one longer. `data` is set only once the move
  // has been played.
  const replied =
    playMove.data === 'played' && game.moves.length > sent.halfMoveNumber
  const thinking = playMove.isPending || (replied && holding)
  if (!thinking) return undefined

  const fen = positionAfter(sent.fen, sent.uci)
  // The recorded game names the player's move once the API has answered. A
  // different move in its place, read again after another tab moved first,
  // is not the player's, so it is left out.
  const answered = game.moves[sent.halfMoveNumber - 1]?.uci === sent.uci
  const moves = game.moves.slice(
    0,
    answered ? sent.halfMoveNumber : sent.halfMoveNumber - 1
  )
  return {
    fen,
    moves,
    lastMove: squaresOf(sent.uci),
    check: answered ? checkSquare(fen, moves) : undefined,
  }
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
