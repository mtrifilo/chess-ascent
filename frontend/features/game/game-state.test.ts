import { describe, expect, it } from 'vitest'

import {
  checkSquare,
  END_REASON_TEXT,
  hintText,
  hintUsedText,
  lastMove,
  moveNumber,
  moveRows,
  nextHalfMoveNumber,
  pieceOn,
  positionAfter,
  promotionChoices,
  resultTitle,
  sideToMoveInCheck,
  toMoveText,
  toUci,
} from './game-state'
import {
  AFTER_E4,
  FOOLS_MATE,
  game,
  MAIA_800,
  move,
  PROMOTION_GAME,
  START_FEN,
} from './test-games'

describe('sideToMoveInCheck', () => {
  it('is false before the first move', () => {
    expect(sideToMoveInCheck([])).toBe(false)
  })

  it('is false after a move that gives no check', () => {
    expect(sideToMoveInCheck([move(1, 'e2e4', 'e4')])).toBe(false)
  })

  it('is true after a check or a checkmate, read from the notation', () => {
    expect(sideToMoveInCheck([move(1, 'f1b5', 'Bb5+')])).toBe(true)
    expect(sideToMoveInCheck([move(1, 'd8h4', 'Qh4#')])).toBe(true)
  })

  it('reads only the latest move', () => {
    expect(
      sideToMoveInCheck([move(1, 'f1b5', 'Bb5+'), move(2, 'c7c6', 'c6')])
    ).toBe(false)
  })
})

describe('checkSquare', () => {
  it("is the square of the side to move's king when it is in check", () => {
    // Fool's mate: White's king on e1 is mated.
    expect(checkSquare(FOOLS_MATE.fen, FOOLS_MATE.moves)).toBe('e1')
  })

  it('is undefined when nobody is in check', () => {
    expect(checkSquare(game().fen, [])).toBeUndefined()
  })
})

describe('moveRows', () => {
  it('pairs White and Black moves under one move number', () => {
    const moves = [
      move(1, 'e2e4', 'e4'),
      move(2, 'e7e5', 'e5'),
      move(3, 'g1f3', 'Nf3'),
    ]

    expect(moveRows(moves)).toEqual([
      { number: 1, white: moves[0], black: moves[1] },
      { number: 2, white: moves[2], black: undefined },
    ])
  })

  it('is empty before the first move', () => {
    expect(moveRows([])).toEqual([])
  })
})

describe('moves in UCI form', () => {
  it('adds the letter of the chosen piece to a promotion', () => {
    expect(toUci({ from: 'b7', to: 'a8' }, 'knight')).toBe('b7a8n')
    expect(toUci({ from: 'e7', to: 'e8' }, 'queen')).toBe('e7e8q')
    expect(toUci({ from: 'e2', to: 'e4' })).toBe('e2e4')
  })

  it('takes the last move from UCI, without a promotion letter', () => {
    expect(lastMove([move(1, 'b7a8n', 'bxa8=N')])).toEqual({
      from: 'b7',
      to: 'a8',
    })
    expect(lastMove([])).toBeUndefined()
  })

  it('lists the pieces a promoting move may choose from', () => {
    expect(promotionChoices(PROMOTION_GAME, { from: 'e7', to: 'e8' })).toEqual([
      'queen',
      'rook',
      'bishop',
      'knight',
    ])
    expect(
      promotionChoices(PROMOTION_GAME, { from: 'e1', to: 'e2' })
    ).toBeUndefined()
  })
})

describe('nextHalfMoveNumber', () => {
  it('is 1 before the first move, and one more than the moves played after', () => {
    expect(nextHalfMoveNumber(game())).toBe(1)
    expect(nextHalfMoveNumber(FOOLS_MATE)).toBe(5)
  })
})

describe('moveNumber', () => {
  it("is the FEN's move number", () => {
    expect(moveNumber(game().fen)).toBe(1)
    expect(
      moveNumber(
        'r1bqk2r/ppp2ppp/2np1n2/2b1p3/2B1P3/2PP1N2/PP3PPP/RNBQK2R w KQkq - 0 6'
      )
    ).toBe(6)
  })
})

describe('positionAfter', () => {
  it('moves the piece and passes the turn, on the same move after White', () => {
    expect(positionAfter(START_FEN, 'e2e4')).toBe(
      'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b - - 0 1'
    )
  })

  it('goes on to the next move after a move by Black', () => {
    expect(positionAfter(AFTER_E4.fen, 'e7e5')).toBe(
      'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w - - 0 2'
    )
  })

  it('takes a captured piece off', () => {
    expect(positionAfter('4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1', 'e4d5')).toBe(
      '4k3/8/8/3P4/8/8/8/4K3 b - - 0 1'
    )
  })

  it('moves the rook too when the king castles, on either side', () => {
    const castling = 'r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1'
    expect(positionAfter(castling, 'e1g1')).toBe(
      'r3k2r/8/8/8/8/8/8/R4RK1 b - - 0 1'
    )
    expect(positionAfter(castling, 'e1c1')).toBe(
      'r3k2r/8/8/8/8/8/8/2KR3R b - - 0 1'
    )
    expect(positionAfter('r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1', 'e8c8')).toBe(
      '2kr3r/8/8/8/8/8/8/R3K2R w - - 0 2'
    )
  })

  it('takes the pawn beside off for an en passant capture', () => {
    expect(positionAfter('4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2', 'e5d6')).toBe(
      '4k3/8/3P4/8/8/8/8/4K3 b - - 0 2'
    )
  })

  it('puts the chosen piece on the last rank for a promotion', () => {
    expect(positionAfter(PROMOTION_GAME.fen, 'e7e8n')).toBe(
      '4N3/6k1/8/8/8/8/8/4K3 b - - 0 1'
    )
  })
})

describe('toMoveText', () => {
  it('words the side to move and the move number', () => {
    expect(toMoveText('white', 6)).toBe('White to move · move 6')
    expect(toMoveText('black', 1)).toBe('Black to move · move 1')
  })
})

describe('result wording', () => {
  it('names the player as the winner of either side when they move both', () => {
    const bothSides = game({ playerSide: 'white' })

    expect(resultTitle(bothSides, 'whiteWins')).toBe('You won')
    expect(resultTitle(bothSides, 'blackWins')).toBe('You won')
    expect(resultTitle(bothSides, 'draw')).toBe('Draw')
  })

  it('names the computer by its strength when it wins', () => {
    const black = game({ playerSide: 'black', opponent: MAIA_800 })

    expect(resultTitle(black, 'whiteWins')).toBe('Maia 800 won')
    expect(resultTitle(black, 'blackWins')).toBe('You won')
  })

  it('has the approved wording for every reason a game can end', () => {
    expect(END_REASON_TEXT).toEqual({
      checkmate: 'Checkmate.',
      stalemate: 'Stalemate.',
      threefoldRepetition: 'Threefold repetition.',
      fiftyMoveRule: 'Fifty-move rule.',
      deadPosition: 'Neither side can checkmate.',
      resignation: 'You resigned.',
    })
  })
})

describe('hintText', () => {
  it('names the piece first, then the move, in the approved wording', () => {
    const knight = { from: 'b1', to: 'd2', piece: 'knight' } as const

    expect(hintText({ ...knight, step: 'piece' })).toBe(
      'Hint: move the knight on b1.'
    )
    expect(hintText({ ...knight, step: 'move' })).toBe(
      'Hint: knight from b1 to d2.'
    )
  })

  it("names a castling move as the king's", () => {
    expect(
      hintText({ from: 'e1', to: 'g1', piece: 'king', step: 'move' })
    ).toBe('Hint: king from e1 to g1.')
  })
})

describe('pieceOn', () => {
  it('reads the kind of piece on a square, or nothing on an empty one', () => {
    expect(pieceOn(START_FEN, 'b1')).toBe('knight')
    expect(pieceOn(START_FEN, 'e8')).toBe('king')
    expect(pieceOn(START_FEN, 'e4')).toBeUndefined()
  })
})

describe('hintUsedText', () => {
  it('marks a game once a hint was given in any position', () => {
    expect(hintUsedText({ hintCount: 0 })).toBeUndefined()
    expect(hintUsedText({ hintCount: 1 })).toBe('Hint used')
    expect(hintUsedText({ hintCount: 3 })).toBe('Hint used')
  })
})
