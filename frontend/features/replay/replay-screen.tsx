'use client'

import { opposite } from '@lichess-org/chessground/util'
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  ChevronsLeftIcon,
  ChevronsRightIcon,
} from 'lucide-react'
import { useEffect, useState } from 'react'

import { ChessBoard } from '@/components/chess-board/chess-board'
import { useNavItemForPage } from '@/components/layout/page-nav-item'
import { Button } from '@/components/ui/button'
import { GameStatus } from '@/features/game/game-status'
import { checkSquare, lastMove, type Game } from '@/features/game/game-state'
import { MoveList } from '@/features/game/move-list'
import { PlayerBar } from '@/features/game/player-bar'

import { replaySummary } from './replay-text'

// A finished game, stepped through move by move on a board that cannot be
// played on, turned to the player's side. Under My games, as drawn.
//
// Each step is a move: the board shows the position after it, as the API
// gives it, and the move list marks it. The steps run from the first move
// to the last, and the screen opens on the last, the game's final position.
// A game with no moves has no step and shows its starting position.
export function ReplayScreen({ game }: { game: Game }) {
  useNavItemForPage('/games')
  const { moves } = game
  const lastStep = moves.length
  const firstStep = Math.min(1, lastStep)
  const [step, setStep] = useState(lastStep)

  const played = moves.slice(0, step)
  const fen = played.at(-1)?.fen ?? game.fen
  const stepTo = (target: number) =>
    setStep(clampStep(target, firstStep, lastStep))

  // The arrow keys step back and forward from anywhere on the page but a
  // field, where they move the caret. A key held with a modifier is left to
  // the browser, which uses some of them to go back or forward a page.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      const delta = ARROW_STEPS[event.key]
      if (
        !delta ||
        event.defaultPrevented ||
        event.altKey ||
        event.ctrlKey ||
        event.metaKey ||
        event.shiftKey ||
        isField(event.target)
      ) {
        return
      }
      event.preventDefault()
      setStep(current => clampStep(current + delta, firstStep, lastStep))
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [firstStep, lastStep])

  const controls = [
    { label: 'First', Icon: ChevronsLeftIcon, target: firstStep },
    { label: 'Previous', Icon: ChevronLeftIcon, target: step - 1 },
    { label: 'Next', Icon: ChevronRightIcon, target: step + 1 },
    { label: 'Last', Icon: ChevronsRightIcon, target: lastStep },
  ]

  return (
    // Laid out as the game screen is (game-screen.tsx): on phones the upper
    // player bar sits against the app shell's top bar, so the shell's top
    // padding is taken back.
    <div className="-mt-4 flex flex-col md:mt-0 md:flex-row md:justify-center md:gap-8">
      <div className="flex min-w-0 flex-col md:w-160 md:gap-2">
        <PlayerBar game={game} side={opposite(game.playerSide)} />
        <ChessBoard
          fen={fen}
          orientation={game.playerSide}
          lastMove={lastMove(played)}
          check={checkSquare(fen, played)}
          readOnly
        />
        <PlayerBar game={game} side={game.playerSide} />
      </div>
      <div className="flex flex-col gap-3 pt-4 md:w-90 md:shrink-0 md:gap-4 md:rounded-md md:border md:bg-card md:p-4">
        <GameStatus title="Replay" detail={replaySummary(game)} />
        {/* Below the move list from `md` up, above it on phones. A control
            that would not change the step is disabled. */}
        <div className="flex gap-2 md:order-last">
          {controls.map(({ label, Icon, target }) => {
            const reachable =
              target !== step &&
              clampStep(target, firstStep, lastStep) === target
            return (
              <Button
                key={label}
                variant="outline"
                aria-label={label}
                className="h-11 min-w-0 flex-1 md:h-8"
                disabled={!reachable}
                onClick={() => stepTo(target)}
              >
                <Icon aria-hidden />
              </Button>
            )
          })}
        </div>
        {/* From `md` up the list takes the height left beside the board and
            scrolls within it, as on the game screen. */}
        <div className="md:relative md:min-h-34 md:flex-1">
          <MoveList
            moves={moves}
            current={step}
            onSelect={stepTo}
            className="md:absolute md:inset-0"
          />
        </div>
      </div>
    </div>
  )
}

// A step is a move's half-move number, so the steps run from `first` to
// `last`, the number of moves.
function clampStep(target: number, first: number, last: number): number {
  return Math.min(last, Math.max(first, target))
}

const ARROW_STEPS: Partial<Record<string, number>> = {
  ArrowLeft: -1,
  ArrowRight: 1,
}

// Where the arrow keys belong to what has focus: a text field, a list to
// choose from, or editable text.
function isField(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable ||
      target.closest('input, textarea, select') !== null)
  )
}
