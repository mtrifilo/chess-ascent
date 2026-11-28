'use client'

import { useRef, useState } from 'react'

import { useAskForHint } from './game-api'
import {
  nextHalfMoveNumber,
  pieceOn,
  type Game,
  type ShownHint,
} from './game-state'

// The hint for the player's move, revealed in two steps from one request:
// the first press asks for it and shows the piece, the second shows the move.
//
// A hint belongs to the position it was asked for in. It is shown only while
// `game` is still in that position and `playersTurn` holds, so it goes when
// the game is read again and has moved on, when the game ends, and while the
// player's move is on its way or the computer thinks; an answer that arrives
// for a position the screen no longer shows is never drawn. `clear` drops it
// for good, together with any answer still on its way, and is called each
// time a move is sent, so it does not come back if that move is not played.
//
// Nothing about a hint is kept outside this component's memory.
export function useHint(game: Game, playersTurn: boolean) {
  const askForHint = useAskForHint(game.id)
  // The position in which the second press revealed the move.
  const [moveShownIn, setMoveShownIn] = useState<string>()
  // The request on its way, set at the press itself, as the mutation reports
  // itself pending only after React has rendered again, which a second press
  // can come before. Each request has its own token, so the end of one that
  // `clear` dropped does not release a later one.
  const asking = useRef<object>(undefined)

  const answer = askForHint.data
  const forThisPosition =
    playersTurn &&
    askForHint.variables?.fen === game.fen &&
    typeof answer === 'object'
      ? answer
      : undefined
  const piece = forThisPosition && pieceOn(game.fen, forThisPosition.from)
  const shown: ShownHint | undefined =
    forThisPosition && piece
      ? {
          ...forThisPosition,
          piece,
          step: moveShownIn === game.fen ? 'move' : 'piece',
        }
      : undefined

  const moveShown = shown?.step === 'move'

  return {
    shown,
    // What the board marks: the piece's square, then both squares and an
    // arrow between them.
    marks: {
      hintSquares: shown && (moveShown ? [shown.from, shown.to] : [shown.from]),
      arrows:
        shown && moveShown ? [{ from: shown.from, to: shown.to }] : undefined,
    },
    // The control can be pressed on the player's turn, except while the API
    // answers and once the move is shown, until the player moves.
    canPress: playersTurn && !askForHint.isPending && !moveShown,
    // The first press asks for the hint; the second, once the piece is
    // shown, reveals the move without asking again.
    press() {
      if (shown) {
        setMoveShownIn(game.fen)
        return
      }
      if (asking.current) return
      const request = {}
      asking.current = request
      // The promise settles even while nothing observes the mutation, as
      // when Next.js keeps the page hidden. A failure is reported by the
      // query cache (query-client.ts); here it only ends the request.
      askForHint
        .mutateAsync({
          halfMoveNumber: nextHalfMoveNumber(game),
          fen: game.fen,
        })
        .catch(() => {})
        .finally(() => {
          if (asking.current === request) asking.current = undefined
        })
    },
    clear() {
      askForHint.reset()
      asking.current = undefined
      setMoveShownIn(undefined)
    },
  }
}
