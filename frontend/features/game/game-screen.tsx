'use client'

import { opposite } from '@lichess-org/chessground/util'
import { ArrowUpDownIcon, CircleAlertIcon } from 'lucide-react'
import { notFound } from 'next/navigation'
import { useRef, useState } from 'react'
import { flushSync } from 'react-dom'

import {
  ChessBoard,
  type BoardMove,
  type Side,
} from '@/components/chess-board/chess-board'
import { useNavItemForPage } from '@/components/layout/page-nav-item'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

import { ColourSwatch } from './colour-swatch'
import { useComputerThinking } from './computer-thinking'
import { TEXT_CONTROL_CLASS } from './controls'
import { useGame, usePlayMove, useResign } from './game-api'
import {
  checkSquare,
  destinations,
  END_REASON_TEXT,
  hintText,
  hintUsedText,
  lastMove,
  moveNumber,
  nextHalfMoveNumber,
  playerName,
  playsComputer,
  promotionChoices,
  resultTitle,
  toMoveText,
  toUci,
  type Game,
  type PromotionPiece,
} from './game-state'
import { useHint } from './hint'
import { HintUsed } from './hint-used'
import { MoveList } from './move-list'
import { NewGameLink } from './new-game-link'
import { PromotionPicker } from './promotion-picker'
import { ResignConfirm } from './resign-confirm'

// One game: the board between the two players' bars, and beside it (below it
// on phones) the game's status or result, the move list and the controls.
//
// It is under Play, as drawn, whether the game is being played or is over,
// and from before the game has loaded. Nothing else shows until then, so the
// page does not flash a starting position. A game the player does not have
// shows the site's not-found page.
export function GameScreen({ id }: { id: string }) {
  useNavItemForPage('/play')
  const { data: game } = useGame(id)
  if (game === null) notFound()
  if (!game) return null
  return <LoadedGame game={game} />
}

// A promotion waiting for the player's choice of piece, with the position it
// was made in, so that a new position read meanwhile (after a move in another
// tab, say) drops it. A move that was not played is dropped the same way.
type PendingPromotion = {
  move: BoardMove
  pieces: PromotionPiece[]
  fen: string
}

function LoadedGame({ game }: { game: Game }) {
  const playMove = usePlayMove(game.id)
  const [flipped, setFlipped] = useState(false)
  // The board shows a move the moment it is made. When the move is not
  // played after all, a new key mounts a fresh board on the position the
  // screen holds, which takes the move back off (see ChessBoard's onMove).
  const [boardKey, setBoardKey] = useState(0)
  const [pending, setPending] = useState<PendingPromotion>()
  const [confirmingResign, setConfirmingResign] = useState(false)
  const resignControl = useRef<HTMLButtonElement>(null)
  // Set at the press itself, as the mutation reports itself pending only
  // after React has rendered again, which a second press can come before.
  // While it is set, the resignation is neither sent again nor cancelled,
  // and no move is sent to race it.
  const resignSent = useRef(false)
  const resign = useResign(game.id, () => {
    resignSent.current = false
  })

  const thinking = useComputerThinking(game, playMove)
  // While the computer thinks, the game is shown as it stood after the
  // player's move, even when its reply has already arrived.
  const isThinking = thinking !== undefined
  const shown = thinking ?? {
    fen: game.fen,
    moves: game.moves,
    lastMove: lastMove(game.moves),
    check: checkSquare(game.fen, game.moves),
  }
  const finished = !isThinking && game.status === 'finished'
  // A move on its way, or the computer's reply to one, holds the board and
  // keeps the game from being resigned until it is shown.
  const moveUnderway = isThinking || playMove.isPending
  // And a resignation on its way holds the board, so that no move is sent
  // to race it.
  const resigning = resign.isPending
  const opponentSide = opposite(game.playerSide)
  const orientation = flipped ? opponentSide : game.playerSide
  // A resignation ends the game without changing the position, so these
  // check that the game is still being played as well as the position.
  // A promotion left waiting when the game ends is dropped, and a new board
  // is mounted to take the pawn the board moved back off; this is done
  // while rendering, so the stale board is never shown.
  if (finished && pending?.fen === game.fen) {
    setPending(undefined)
    setBoardKey(key => key + 1)
  }
  const promotion = !finished && pending?.fen === game.fen ? pending : undefined
  const readOnly = finished || moveUnderway || resigning
  // Only the player's turn has a use for a hint: not while a move or a
  // resignation is on its way or the computer thinks, nor while a promotion
  // waits for its piece.
  const playersTurn =
    !readOnly && !promotion && game.sideToMove === game.playerSide
  // While the resign confirmation stands in for the controls, Hint among
  // them, a hint is kept but not shown, as the confirmation is drawn without
  // one; Cancel shows it again.
  const hint = useHint(game, playersTurn && !confirmingResign)
  const hintUsed = hintUsedText(game)
  const failedMove =
    !finished && playMove.isError && playMove.variables.fen === game.fen
      ? playMove.variables.uci
      : undefined

  const status = thinking
    ? {
        title: `${playerName(game, opponentSide)} is thinking`,
        detail: toMoveText(opponentSide, moveNumber(thinking.fen)),
      }
    : {
        title: 'Your move',
        detail: promotion
          ? `Choose a piece for the pawn on ${promotion.move.to}.`
          : hint.shown
            ? hintText(hint.shown)
            : toMoveText(game.sideToMove, moveNumber(game.fen)),
      }

  function send(uci: string) {
    const resetBoard = () => setBoardKey(key => key + 1)
    if (resignSent.current) {
      resetBoard()
      return
    }
    // A move refused above was not sent, so the hint stays.
    hint.clear()
    playMove.mutate(
      { uci, halfMoveNumber: nextHalfMoveNumber(game), fen: game.fen },
      {
        onSuccess: outcome => {
          if (outcome === 'reloaded') resetBoard()
        },
        onError: resetBoard,
      }
    )
  }

  function confirmResign() {
    if (moveUnderway || resignSent.current) return
    resignSent.current = true
    resign.mutate()
  }

  function cancelResign() {
    if (resignSent.current) return
    // The Resign control is back in the page once this returns.
    flushSync(() => setConfirmingResign(false))
    resignControl.current?.focus()
  }

  function onMove(move: BoardMove) {
    const pieces = promotionChoices(game, move)
    if (pieces) setPending({ move, pieces, fen: game.fen })
    else send(toUci(move))
  }

  return (
    // On phones the upper player bar sits against the app shell's top bar,
    // as drawn, so the shell's 16px top padding (app-shell.tsx) is taken
    // back; change the two together.
    <div className="-mt-4 flex flex-col md:mt-0 md:flex-row md:justify-center md:gap-8">
      <div className="flex min-w-0 flex-col md:w-160 md:gap-2">
        <PlayerBar
          game={game}
          side={opposite(orientation)}
          thinking={isThinking && opposite(orientation) === opponentSide}
        />
        <div className="relative">
          <ChessBoard
            key={boardKey}
            fen={shown.fen}
            orientation={orientation}
            destinations={destinations(game)}
            lastMove={shown.lastMove}
            check={shown.check}
            {...hint.marks}
            readOnly={readOnly}
            onMove={onMove}
          />
          {promotion && (
            <PromotionPicker
              square={promotion.move.to}
              side={game.sideToMove}
              orientation={orientation}
              pieces={promotion.pieces}
              onChoose={piece => {
                // Refused, and left waiting, while a resignation is on its
                // way: it is chosen again if the resignation fails, and
                // dropped with the game if it does not.
                if (resignSent.current) return
                setPending(undefined)
                send(toUci(promotion.move, piece))
              }}
            />
          )}
        </div>
        <PlayerBar
          game={game}
          side={orientation}
          thinking={isThinking && orientation === opponentSide}
        />
      </div>
      <div className="flex flex-col gap-3 pt-4 md:w-90 md:shrink-0 md:gap-4 md:rounded-md md:border md:bg-card md:p-4">
        {finished ? <ResultPanel game={game} /> : <GameStatus {...status} />}
        {hintUsed && <HintUsed className="self-start">{hintUsed}</HintUsed>}
        {failedMove && <MoveFailed onTryAgain={() => send(failedMove)} />}
        {/* The controls, or the resign confirmation in their place: below
            the move list from `md` up, above it on phones. A game that ends
            while the confirmation is open, in another tab say, shows its
            result and no confirmation. */}
        {confirmingResign && !finished ? (
          <ResignConfirm
            moveUnderway={moveUnderway}
            sending={resigning}
            onCancel={cancelResign}
            onResign={confirmResign}
            className="md:order-last"
          />
        ) : (
          <div className="flex items-center gap-2 md:order-last md:h-25">
            {/* Only a game against the computer has a hint, and only while
                it is being played. Once the move is shown the control rests
                until the player moves. A press it cannot act on is refused
                through aria-disabled rather than `disabled`, so focus stays
                on it, and it takes the button's disabled look, as the
                thinking state draws it. */}
            {playsComputer(game) && !finished && (
              <Button
                aria-disabled={!hint.canPress}
                className={cn(
                  TEXT_CONTROL_CLASS,
                  'flex-1 aria-disabled:pointer-events-none aria-disabled:opacity-50 md:flex-none'
                )}
                onClick={() => {
                  if (hint.canPress) hint.press()
                }}
              >
                {hint.shown?.step === 'piece' ? 'Show move' : 'Hint'}
              </Button>
            )}
            {!finished && (
              <Button
                ref={resignControl}
                variant="outline"
                aria-disabled={moveUnderway}
                className={TEXT_CONTROL_CLASS}
                onClick={() => {
                  if (!moveUnderway) setConfirmingResign(true)
                }}
              >
                Resign
              </Button>
            )}
            <Button
              variant="outline"
              aria-label="Flip board"
              className="ml-auto size-12.75 md:size-8"
              onClick={() => setFlipped(value => !value)}
            >
              <ArrowUpDownIcon aria-hidden />
            </Button>
          </div>
        )}
        {/* From `md` up the list takes the height left beside the board and
            scrolls within it. Its box is absolutely placed, so the moves
            never make the panel, and with it the row, taller than the
            board. It keeps room for its head and four rows (136px) even
            so: where a narrow window shrinks the board and the alert shows,
            the panel grows instead. */}
        <div className="md:relative md:min-h-34 md:flex-1">
          <MoveList moves={shown.moves} className="md:absolute md:inset-0" />
        </div>
      </div>
    </div>
  )
}

// One side's player, and whether that player is the computer thinking.
function PlayerBar({
  game,
  side,
  thinking,
}: {
  game: Game
  side: Side
  thinking: boolean
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

function GameStatus({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <h1 className="font-display text-display-s font-semibold">{title}</h1>
      <p className="text-body-s text-muted-foreground">{detail}</p>
    </div>
  )
}

function ResultPanel({ game }: { game: Game }) {
  return (
    <div className="flex flex-col gap-3 rounded-md border bg-card p-4">
      <div className="flex flex-col gap-0.5">
        <h1 className="font-display text-display-s font-semibold">
          {game.result && resultTitle(game, game.result)}
        </h1>
        <p className="text-sm text-muted-foreground">
          {game.endReason && END_REASON_TEXT[game.endReason]}
        </p>
      </div>
      <NewGameLink />
    </div>
  )
}

// One alert for a move the API refused and for one that never reached it.
// Its text is set on 20px lines, as drawn.
function MoveFailed({ onTryAgain }: { onTryAgain: () => void }) {
  return (
    <Alert variant="destructive" className="gap-y-2">
      <CircleAlertIcon aria-hidden />
      <AlertTitle className="tracking-normal">
        Your move was not sent
      </AlertTitle>
      <AlertDescription className="gap-2 [&_p]:leading-5">
        <p>
          The connection to the server was lost. The board shows the position
          before your move.
        </p>
        <Button
          variant="outline"
          className="h-12.75 px-4 text-foreground md:h-8 md:px-2"
          onClick={onTryAgain}
        >
          Try again
        </Button>
      </AlertDescription>
    </Alert>
  )
}
