'use client'

import { opposite } from '@lichess-org/chessground/util'
import { ArrowUpDownIcon, CircleAlertIcon } from 'lucide-react'
import Link from 'next/link'
import { notFound } from 'next/navigation'
import { useState } from 'react'

import {
  ChessBoard,
  type BoardMove,
  type Side,
} from '@/components/chess-board/chess-board'
import { useNavItemForPage } from '@/components/layout/page-nav-item'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

import { useComputerThinking } from './computer-thinking'
import { useGame, usePlayMove } from './game-api'
import {
  checkSquare,
  destinations,
  END_REASON_TEXT,
  lastMove,
  moveNumber,
  nextHalfMoveNumber,
  playerName,
  promotionChoices,
  resultTitle,
  toMoveText,
  toUci,
  type Game,
  type PromotionPiece,
} from './game-state'
import { MoveList } from './move-list'
import { PromotionPicker } from './promotion-picker'

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
  const opponentSide = opposite(game.playerSide)
  const orientation = flipped ? opponentSide : game.playerSide
  const promotion = pending?.fen === game.fen ? pending : undefined
  const failedMove =
    playMove.isError && playMove.variables.fen === game.fen
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
          : toMoveText(game.sideToMove, moveNumber(game.fen)),
      }

  function send(uci: string) {
    const resetBoard = () => setBoardKey(key => key + 1)
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
            readOnly={finished || isThinking || playMove.isPending}
            onMove={onMove}
          />
          {promotion && (
            <PromotionPicker
              square={promotion.move.to}
              side={game.sideToMove}
              orientation={orientation}
              pieces={promotion.pieces}
              onChoose={piece => {
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
        {failedMove && <MoveFailed onTryAgain={() => send(failedMove)} />}
        {/* Below the move list from `md` up, above it on phones. */}
        <div className="flex items-center justify-end md:order-last md:h-25">
          <Button
            variant="outline"
            aria-label="Flip board"
            className="size-12.75 md:size-8"
            onClick={() => setFlipped(value => !value)}
          >
            <ArrowUpDownIcon aria-hidden />
          </Button>
        </div>
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
      <span
        aria-hidden
        className={cn(
          'size-3.5 rounded-sm border border-separator',
          side === 'white' ? 'bg-piece-white' : 'bg-piece-black'
        )}
      />
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
      <Button
        asChild
        className="h-12.75 w-full md:h-8 md:w-auto md:self-start md:px-2"
      >
        <Link href="/play">New game</Link>
      </Button>
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
