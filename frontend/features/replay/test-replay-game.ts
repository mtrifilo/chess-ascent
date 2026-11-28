import type { Game } from '@/features/game/game-state'

// The API's own answer to GET /games/{id} for a finished game, saved as it
// came: a both-sides game in which the moves of SpecialMoves in the API tests
// (backend/tests/ChessAscent.Api.Tests/Features/Games/GameRequests.cs) were
// played through the API. It holds an en passant capture (3. exf6), castling
// on each side (6... O-O-O, 7. O-O), a promotion (8. gxh8=Q) and an
// under-promotion (11... bxa1=N), and White mates with 14. Qxf8#.
// Every position the replay shows is the one the API sent for that move, so
// none is typed by hand. Imported only by tests.
export const SPECIAL_MOVES_GAME: Game = {
  id: '01a2204c-494c-73b2-b897-cd50e036ff39',
  playerSide: 'white',
  opponent: { kind: 'none', strength: null },
  fen: '3k1Q2/1pprp2p/2nqb3/3p4/8/2NPBN2/P1P1BPPP/n2Q1RK1 b - - 0 14',
  sideToMove: 'black',
  status: 'finished',
  result: 'whiteWins',
  endReason: 'checkmate',
  moves: [
    {
      halfMoveNumber: 1,
      uci: 'e2e4',
      san: 'e4',
      fen: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1',
    },
    {
      halfMoveNumber: 2,
      uci: 'd7d5',
      san: 'd5',
      fen: 'rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2',
    },
    {
      halfMoveNumber: 3,
      uci: 'e4e5',
      san: 'e5',
      fen: 'rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2',
    },
    {
      halfMoveNumber: 4,
      uci: 'f7f5',
      san: 'f5',
      fen: 'rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3',
    },
    {
      halfMoveNumber: 5,
      uci: 'e5f6',
      san: 'exf6',
      fen: 'rnbqkbnr/ppp1p1pp/5P2/3p4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 3',
    },
    {
      halfMoveNumber: 6,
      uci: 'b8c6',
      san: 'Nc6',
      fen: 'r1bqkbnr/ppp1p1pp/2n2P2/3p4/8/8/PPPP1PPP/RNBQKBNR w KQkq - 1 4',
    },
    {
      halfMoveNumber: 7,
      uci: 'f6g7',
      san: 'fxg7',
      fen: 'r1bqkbnr/ppp1p1Pp/2n5/3p4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 4',
    },
    {
      halfMoveNumber: 8,
      uci: 'c8e6',
      san: 'Be6',
      fen: 'r2qkbnr/ppp1p1Pp/2n1b3/3p4/8/8/PPPP1PPP/RNBQKBNR w KQkq - 1 5',
    },
    {
      halfMoveNumber: 9,
      uci: 'g1f3',
      san: 'Nf3',
      fen: 'r2qkbnr/ppp1p1Pp/2n1b3/3p4/8/5N2/PPPP1PPP/RNBQKB1R b KQkq - 2 5',
    },
    {
      halfMoveNumber: 10,
      uci: 'd8d6',
      san: 'Qd6',
      fen: 'r3kbnr/ppp1p1Pp/2nqb3/3p4/8/5N2/PPPP1PPP/RNBQKB1R w KQkq - 3 6',
    },
    {
      halfMoveNumber: 11,
      uci: 'f1e2',
      san: 'Be2',
      fen: 'r3kbnr/ppp1p1Pp/2nqb3/3p4/8/5N2/PPPPBPPP/RNBQK2R b KQkq - 4 6',
    },
    {
      halfMoveNumber: 12,
      uci: 'e8c8',
      san: 'O-O-O',
      fen: '2kr1bnr/ppp1p1Pp/2nqb3/3p4/8/5N2/PPPPBPPP/RNBQK2R w KQ - 5 7',
    },
    {
      halfMoveNumber: 13,
      uci: 'e1g1',
      san: 'O-O',
      fen: '2kr1bnr/ppp1p1Pp/2nqb3/3p4/8/5N2/PPPPBPPP/RNBQ1RK1 b - - 6 7',
    },
    {
      halfMoveNumber: 14,
      uci: 'a7a5',
      san: 'a5',
      fen: '2kr1bnr/1pp1p1Pp/2nqb3/p2p4/8/5N2/PPPPBPPP/RNBQ1RK1 w - a6 0 8',
    },
    {
      halfMoveNumber: 15,
      uci: 'g7h8q',
      san: 'gxh8=Q',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/p2p4/8/5N2/PPPPBPPP/RNBQ1RK1 b - - 0 8',
    },
    {
      halfMoveNumber: 16,
      uci: 'a5a4',
      san: 'a4',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/3p4/p7/5N2/PPPPBPPP/RNBQ1RK1 w - - 0 9',
    },
    {
      halfMoveNumber: 17,
      uci: 'd2d3',
      san: 'd3',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/3p4/p7/3P1N2/PPP1BPPP/RNBQ1RK1 b - - 0 9',
    },
    {
      halfMoveNumber: 18,
      uci: 'a4a3',
      san: 'a3',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/3p4/8/p2P1N2/PPP1BPPP/RNBQ1RK1 w - - 0 10',
    },
    {
      halfMoveNumber: 19,
      uci: 'b1c3',
      san: 'Nc3',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/3p4/8/p1NP1N2/PPP1BPPP/R1BQ1RK1 b - - 1 10',
    },
    {
      halfMoveNumber: 20,
      uci: 'a3b2',
      san: 'axb2',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/3p4/8/2NP1N2/PpP1BPPP/R1BQ1RK1 w - - 0 11',
    },
    {
      halfMoveNumber: 21,
      uci: 'c1e3',
      san: 'Be3',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/3p4/8/2NPBN2/PpP1BPPP/R2Q1RK1 b - - 1 11',
    },
    {
      halfMoveNumber: 22,
      uci: 'b2a1n',
      san: 'bxa1=N',
      fen: '2kr1bnQ/1pp1p2p/2nqb3/3p4/8/2NPBN2/P1P1BPPP/n2Q1RK1 w - - 0 12',
    },
    {
      halfMoveNumber: 23,
      uci: 'h8g8',
      san: 'Qxg8',
      fen: '2kr1bQ1/1pp1p2p/2nqb3/3p4/8/2NPBN2/P1P1BPPP/n2Q1RK1 b - - 0 12',
    },
    {
      halfMoveNumber: 24,
      uci: 'd8d7',
      san: 'Rd7',
      fen: '2k2bQ1/1pprp2p/2nqb3/3p4/8/2NPBN2/P1P1BPPP/n2Q1RK1 w - - 1 13',
    },
    {
      halfMoveNumber: 25,
      uci: 'g8h8',
      san: 'Qh8',
      fen: '2k2b1Q/1pprp2p/2nqb3/3p4/8/2NPBN2/P1P1BPPP/n2Q1RK1 b - - 2 13',
    },
    {
      halfMoveNumber: 26,
      uci: 'c8d8',
      san: 'Kd8',
      fen: '3k1b1Q/1pprp2p/2nqb3/3p4/8/2NPBN2/P1P1BPPP/n2Q1RK1 w - - 3 14',
    },
    {
      halfMoveNumber: 27,
      uci: 'h8f8',
      san: 'Qxf8#',
      fen: '3k1Q2/1pprp2p/2nqb3/3p4/8/2NPBN2/P1P1BPPP/n2Q1RK1 b - - 0 14',
    },
  ],
  legalMoves: {},
  promotions: [],
  hintCount: 0,
  lastActivityAt: '2026-11-28T03:56:40.308125+00:00',
}
