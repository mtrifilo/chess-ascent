'use client'

import { ArrowUpDownIcon, CircleAlertIcon } from 'lucide-react'
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

import { useGame, usePlayMove } from './game-api'
import {
  checkSquare,
  destinations,
  END_REASON_TEXT,
  lastMove,
  moveNumber,
  playerName,
  promotionChoices,
  resultTitle,
  sideLabel,
  toUci,
  type Game,
  type PromotionPiece,
} from './game-state'
import { MoveList } from './move-list'
import { NewGameButton } from './new-game-button'
import { PromotionPicker } from './promotion-picker'

// One game: the board between the two players' bars, and beside it (below it
// on phones) the game's status or result, the move list and the controls.
//
// Nothing shows until the game has loaded, so the page does not flash a
// starting position. A game the player does not have shows the site's
// not-found page.
export function GameScreen({ id }: { id: string }) {
  const { data: game } = useGame(id)
  useNavItemForPage(
    game ? (game.status === 'finished' ? '/games' : '/play') : undefined
  )
  if (game === null) notFound()
  if (!game) return null
  return <LoadedGame game={game} />
}

// A promotion waiting for the player's choice, in the position it was made
// in, so that a new position read meanwhile (from another tab, say) drops it.
type PendingPromotion = { move: BoardMove; fen: string }

function LoadedGame({ game }: { game: Game }) {
  const playMove = usePlayMove(game.id)
  const [flipped, setFlipped] = useState(false)
  // The board shows a move the moment it is made. When the move is not
  // played after all, a new key mounts a fresh board on the position the
  // screen holds, which takes the move back off (see ChessBoard's onMove).
  const [boardKey, setBoardKey] = useState(0)
  const [pending, setPending] = useState<PendingPromotion>()

  const finished = game.status === 'finished'
  const orientation = flipped ? opposite(game.playerSide) : game.playerSide
  const promotion = pending?.fen === game.fen ? pending.move : undefined

  function send(uci: string) {
    const resetBoard = () => setBoardKey(key => key + 1)
    playMove.mutate(uci, {
      onSuccess: outcome => {
        if (outcome === 'reloaded') resetBoard()
      },
      onError: resetBoard,
    })
  }

  function onMove(move: BoardMove) {
    if (promotionChoices(game, move)) setPending({ move, fen: game.fen })
    else send(toUci(move))
  }

  function onChoose(piece: PromotionPiece) {
    if (!promotion) return
    setPending(undefined)
    send(toUci(promotion, piece))
  }

  const failedMove = playMove.isError ? playMove.variables : undefined

  return (
    <div className="-mt-4 flex flex-col md:mt-0 md:flex-row md:justify-center md:gap-8">
      <div className="flex min-w-0 flex-col md:w-160 md:gap-2">
        <PlayerBar game={game} side={opposite(orientation)} />
        <div className="relative">
          <ChessBoard
            key={boardKey}
            fen={game.fen}
            orientation={orientation}
            destinations={destinations(game)}
            lastMove={lastMove(game.moves)}
            check={checkSquare(game)}
            readOnly={finished || playMove.isPending}
            onMove={onMove}
          />
          {promotion && (
            <PromotionPicker
              square={promotion.to}
              side={game.sideToMove}
              orientation={orientation}
              pieces={promotionChoices(game, promotion) ?? []}
              onChoose={onChoose}
            />
          )}
        </div>
        <PlayerBar game={game} side={orientation} />
      </div>
      <div className="flex flex-col gap-3 pt-4 md:w-90 md:shrink-0 md:gap-4 md:rounded-md md:border md:bg-card md:p-4">
        {finished ? (
          <ResultPanel game={game} />
        ) : (
          <GameStatus
            detail={
              promotion
                ? `Choose a piece for the pawn on ${promotion.to}.`
                : `${sideLabel(game.sideToMove)} to move · move ${moveNumber(game.fen)}`
            }
          />
        )}
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
        <MoveList moves={game.moves} className="md:min-h-0 md:flex-1" />
      </div>
    </div>
  )
}

function PlayerBar({ game, side }: { game: Game; side: Side }) {
  return (
    <div className="flex h-10 items-center gap-2">
      <span
        aria-hidden
        className={
          side === 'white'
            ? 'size-3.5 rounded-sm border border-separator bg-piece-white'
            : 'size-3.5 rounded-sm border border-separator bg-piece-black'
        }
      />
      <span className="text-sm font-semibold">{playerName(game, side)}</span>
    </div>
  )
}

function GameStatus({ detail }: { detail: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <h1 className="font-display text-display-s font-semibold">Your move</h1>
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
      <NewGameButton className="h-12.75 w-full md:h-8 md:w-auto md:self-start md:px-2" />
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

function opposite(side: Side): Side {
  return side === 'white' ? 'black' : 'white'
}
