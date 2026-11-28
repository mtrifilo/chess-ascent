import type { Game } from './game-state'

// Games as the API answers them, for tests. Imported only by tests.

export const GAME_ID = '3f2b1c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d'

export const START_FEN =
  'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1'

export function move(halfMoveNumber: number, uci: string, san: string) {
  return { halfMoveNumber, uci, san }
}

// A both-sides game at the starting position, with some of White's legal
// moves; `overrides` replaces any field.
export function game(overrides: Partial<Game> = {}): Game {
  return {
    id: GAME_ID,
    playerSide: 'white',
    opponent: { kind: 'none', strength: null },
    fen: START_FEN,
    sideToMove: 'white',
    status: 'ongoing',
    result: null,
    endReason: null,
    moves: [],
    legalMoves: { e2: ['e3', 'e4'], g1: ['f3', 'h3'] },
    promotions: [],
    ...overrides,
  }
}

// The game after 1. e4, Black to move.
export const AFTER_E4 = game({
  fen: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1',
  sideToMove: 'black',
  moves: [move(1, 'e2e4', 'e4')],
  legalMoves: { e7: ['e6', 'e5'] },
})

// The game after 1. e4 e5, White to move: two moves on from the start, as
// after one request that played a move and an opponent's reply, or after
// another tab played twice.
export const AFTER_E4_E5 = game({
  fen: 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2',
  moves: [move(1, 'e2e4', 'e4'), move(2, 'e7e5', 'e5')],
  legalMoves: { g1: ['f3', 'h3', 'e2'] },
})

// White's pawn on e7 can promote on e8, as in the designs.
export const PROMOTION_GAME = game({
  fen: '8/4P1k1/8/8/8/8/8/4K3 w - - 0 1',
  legalMoves: { e7: ['e8'], e1: ['d1', 'd2', 'e2', 'f2', 'f1'] },
  promotions: [
    { from: 'e7', to: 'e8', pieces: ['queen', 'rook', 'bishop', 'knight'] },
  ],
})

// Fool's mate: Black has mated White.
export const FOOLS_MATE = game({
  fen: 'rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3',
  sideToMove: 'white',
  status: 'finished',
  result: 'blackWins',
  endReason: 'checkmate',
  moves: [
    move(1, 'f2f3', 'f3'),
    move(2, 'e7e5', 'e5'),
    move(3, 'g2g4', 'g4'),
    move(4, 'd8h4', 'Qh4#'),
  ],
  legalMoves: {},
})
