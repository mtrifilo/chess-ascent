import {
  read as readFen,
  write as writePlacement,
} from '@lichess-org/chessground/fen'
import { key2pos, pos2key } from '@lichess-org/chessground/util'

import type {
  BoardMove,
  Side,
  Square,
} from '@/components/chess-board/chess-board'
import type { components } from '@/types/api'

// What the game screen shows, worked out from the API's answer alone. The
// server is the authority on the rules: nothing here decides whether a move
// is legal.

export type Game = components['schemas']['GameResponse']
type Move = components['schemas']['MoveResponse']
type Result = components['schemas']['GameResult']
type EndReason = components['schemas']['GameEndReason']
export type PromotionPiece = components['schemas']['PromotionPiece']

// Whether the side to move is in check. The API has no field for it, so it
// is read from the latest move's standard notation, which ends in `+` for a
// check and `#` for a checkmate. Before the first move nobody is in check.
// A field from the API would replace this function.
export function sideToMoveInCheck(moves: readonly Move[]): boolean {
  const latest = moves.at(-1)?.san
  return latest !== undefined && /[+#]$/.test(latest)
}

// The square of the king the board marks as in check, if any: the king of
// the side to move in `fen`, when the latest of `moves` gave check.
export function checkSquare(
  fen: string,
  moves: readonly Move[]
): Square | undefined {
  if (!sideToMoveInCheck(moves)) return undefined
  const side = sideToMove(fen)
  for (const [square, piece] of readFen(fen)) {
    if (piece.role === 'king' && piece.color === side) {
      return square as Square
    }
  }
  return undefined
}

// The FEN's second field.
function sideToMove(fen: string): Side {
  return fen.split(' ')[1] === 'b' ? 'black' : 'white'
}

// The place in the game the next move takes, counted from 1 for the first
// move, whichever side made it: one more than the moves played so far.
export function nextHalfMoveNumber(game: Game): number {
  return game.moves.length + 1
}

export function lastMove(moves: readonly Move[]): BoardMove | undefined {
  const latest = moves.at(-1)
  return latest && squaresOf(latest.uci)
}

// The squares a UCI move leaves and arrives on; a promotion's piece letter,
// the fifth character, is not one of them.
export function squaresOf(uci: string): BoardMove {
  return { from: uci.slice(0, 2) as Square, to: uci.slice(2, 4) as Square }
}

export function destinations(game: Game): Map<Square, Square[]> {
  return new Map(
    Object.entries(game.legalMoves).map(([from, to]) => [
      from as Square,
      to as Square[],
    ])
  )
}

// The pieces the player may choose from when the move promotes a pawn, or
// undefined when it does not.
export function promotionChoices(
  game: Game,
  move: BoardMove
): PromotionPiece[] | undefined {
  return game.promotions.find(
    promotion => promotion.from === move.from && promotion.to === move.to
  )?.pieces
}

const PROMOTION_LETTERS: Record<PromotionPiece, string> = {
  queen: 'q',
  rook: 'r',
  bishop: 'b',
  knight: 'n',
}

export function toUci(move: BoardMove, promotion?: PromotionPiece): string {
  return move.from + move.to + (promotion ? PROMOTION_LETTERS[promotion] : '')
}

const PROMOTION_PIECES = Object.fromEntries(
  Object.entries(PROMOTION_LETTERS).map(([piece, letter]) => [letter, piece])
) as Record<string, PromotionPiece>

// The position after the move `uci` is made in `fen`, as the board shows it
// while the API has not yet answered with the position itself. The pieces
// move as on the board: castling moves the rook too, an en passant capture
// takes the pawn beside, and a promotion puts the chosen piece on the last
// rank. The turn passes and the move number goes on after a move by Black.
// Nothing is checked: the move is one the API listed as legal. Castling
// rights and the en passant square are left out, since nothing that shows
// this position reads them.
export function positionAfter(fen: string, uci: string): string {
  const pieces = readFen(fen)
  const { from, to } = squaresOf(uci)
  const piece = pieces.get(from)
  if (piece) {
    const [fromFile, fromRank] = key2pos(from)
    const [toFile] = key2pos(to)
    pieces.delete(from)
    if (piece.role === 'king' && Math.abs(toFile - fromFile) === 2) {
      const rookFrom = pos2key([toFile > fromFile ? 7 : 0, fromRank])
      const rookTo = pos2key([(fromFile + toFile) / 2, fromRank])
      const rook = rookFrom && pieces.get(rookFrom)
      if (rook && rookFrom && rookTo) {
        pieces.delete(rookFrom)
        pieces.set(rookTo, rook)
      }
    }
    if (piece.role === 'pawn' && toFile !== fromFile && !pieces.has(to)) {
      const passed = pos2key([toFile, fromRank])
      if (passed) pieces.delete(passed)
    }
    const promotion = PROMOTION_PIECES[uci.slice(4)]
    pieces.set(to, promotion ? { ...piece, role: promotion } : piece)
  }
  const mover = sideToMove(fen)
  const number = moveNumber(fen) + (mover === 'black' ? 1 : 0)
  const next = mover === 'white' ? 'b' : 'w'
  return `${writePlacement(pieces)} ${next} - - 0 ${number}`
}

// One line of the move list: a move number with White's move and Black's
// reply. Every game starts from the standard starting position, so White
// makes the first move and each pair of moves is one numbered move.
export type MoveRow = { number: number; white: Move; black?: Move }

export function moveRows(moves: readonly Move[]): MoveRow[] {
  const rows: MoveRow[] = []
  for (let index = 0; index < moves.length; index += 2) {
    rows.push({
      number: index / 2 + 1,
      white: moves[index],
      black: moves[index + 1],
    })
  }
  return rows
}

// The FEN's last field: the number of the move being played, which starts at
// 1 and goes up after each move by Black.
export function moveNumber(fen: string): number {
  return Number(fen.split(' ')[5])
}

export function sideLabel(side: Side): 'White' | 'Black' {
  return side === 'white' ? 'White' : 'Black'
}

// The status line under the heading, such as "White to move · move 6".
export function toMoveText(side: Side, number: number): string {
  return `${sideLabel(side)} to move · move ${number}`
}

// The computer opponent's strengths the API accepts, weakest first, and the
// one a new game offers first.
export const OPPONENT_STRENGTHS = [600, 800, 1000, 1200, 1400] as const
export type OpponentStrength = (typeof OPPONENT_STRENGTHS)[number]
export const DEFAULT_OPPONENT_STRENGTH: OpponentStrength = 800

export function playsComputer(game: Game): boolean {
  return game.opponent.kind === 'computer'
}

// The name the screen gives the player of a side. When the signed-in player
// moves both sides, both are theirs. The computer is named after its
// strength, as the API reports it.
export function playerName(game: Game, side: Side): string {
  if (side === game.playerSide || !playsComputer(game)) return 'You'
  return `Maia ${game.opponent.strength}`
}

// The heading of the result panel.
export function resultTitle(game: Game, result: Result): string {
  if (result === 'draw') return 'Draw'
  return `${playerName(game, result === 'whiteWins' ? 'white' : 'black')} won`
}

export const END_REASON_TEXT: Record<EndReason, string> = {
  checkmate: 'Checkmate.',
  stalemate: 'Stalemate.',
  threefoldRepetition: 'Threefold repetition.',
  fiftyMoveRule: 'Fifty-move rule.',
  deadPosition: 'Neither side can checkmate.',
  resignation: 'You resigned.',
}
