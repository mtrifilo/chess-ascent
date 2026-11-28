import { describe, expect, it } from 'vitest'

import {
  checkSquare,
  END_REASON_TEXT,
  lastMove,
  moveNumber,
  moveRows,
  nextHalfMoveNumber,
  promotionChoices,
  resultTitle,
  sideToMoveInCheck,
  toUci,
} from './game-state'
import { FOOLS_MATE, game, move, PROMOTION_GAME } from './test-games'

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
    expect(checkSquare(FOOLS_MATE)).toBe('e1')
  })

  it('is undefined when nobody is in check', () => {
    expect(checkSquare(game())).toBeUndefined()
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

describe('result wording', () => {
  it('names the player as the winner of either side when they move both', () => {
    const bothSides = game({ playerSide: 'white' })

    expect(resultTitle(bothSides, 'whiteWins')).toBe('You won')
    expect(resultTitle(bothSides, 'blackWins')).toBe('You won')
    expect(resultTitle(bothSides, 'draw')).toBe('Draw')
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
