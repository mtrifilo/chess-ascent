import type { Side } from '@/components/chess-board/chess-board'

import { ColourSwatch } from './colour-swatch'
import { playerName, type Game } from './game-state'

// One side's player, above or below the board, and whether that player is
// the computer thinking.
export function PlayerBar({
  game,
  side,
  thinking = false,
}: {
  game: Game
  side: Side
  thinking?: boolean
}) {
  return (
    <div className="flex h-10 items-center gap-2">
      <ColourSwatch side={side} />
      <span className="text-sm font-semibold">{playerName(game, side)}</span>
      {thinking && (
        <span className="ml-auto text-body-s text-muted-foreground">
          Thinking…
        </span>
      )}
    </div>
  )
}
