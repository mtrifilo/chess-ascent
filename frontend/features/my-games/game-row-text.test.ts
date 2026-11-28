import { describe, expect, it } from 'vitest'

import { dateText, gameRowText, resultText } from './game-row-text'
import { finishedGame, unfinishedGame } from './test-game-lists'

describe('resultText', () => {
  it.each([
    [0, 'In progress, move 1'],
    [1, 'In progress, move 1'],
    [2, 'In progress, move 2'],
    [10, 'In progress, move 6'],
    [11, 'In progress, move 6'],
    [22, 'In progress, move 12'],
  ])(
    'counts the move an unfinished game is on, after %i half-moves',
    (halfMoveCount, expected) => {
      expect(resultText(unfinishedGame({ halfMoveCount }))).toBe(expected)
    }
  )

  it.each([
    ['white', 'whiteWins', 'Won'],
    ['black', 'blackWins', 'Won'],
    ['white', 'blackWins', 'Lost'],
    ['black', 'whiteWins', 'Lost'],
    ['white', 'draw', 'Draw'],
    ['black', 'draw', 'Draw'],
  ] as const)(
    'words a finished game as the player with %s saw a result of %s',
    (playerSide, result, expected) => {
      const game = finishedGame({ playerSide, result, endReason: 'checkmate' })
      expect(resultText(game)).toBe(`${expected}, checkmate`)
    }
  )

  it.each([
    ['checkmate', 'whiteWins', 'Won, checkmate'],
    ['checkmate', 'blackWins', 'Lost, checkmate'],
    ['resignation', 'blackWins', 'Lost, resigned'],
    ['stalemate', 'draw', 'Draw, stalemate'],
    ['threefoldRepetition', 'draw', 'Draw, threefold repetition'],
    ['fiftyMoveRule', 'draw', 'Draw, fifty-move rule'],
    ['deadPosition', 'draw', 'Draw, neither side can checkmate'],
  ] as const)('words the end reason %s', (endReason, result, expected) => {
    const game = finishedGame({ playerSide: 'white', result, endReason })
    expect(resultText(game)).toBe(expected)
  })
})

describe('dateText', () => {
  // 03:00 on 26 November in UTC is still 25 November on the west coast of
  // the United States, and already midday in Tokyo.
  const LATE_UTC_NIGHT = '2026-11-26T03:00:00Z'

  it.each([
    ['UTC', '26 Nov 2026'],
    ['America/Los_Angeles', '25 Nov 2026'],
    ['Asia/Tokyo', '26 Nov 2026'],
  ])('gives the day in %s', (timeZone, expected) => {
    expect(dateText(LATE_UTC_NIGHT, timeZone)).toBe(expected)
  })

  it.each([
    ['2026-01-05T12:00:00Z', '5 Jan 2026'],
    ['2026-05-31T12:00:00Z', '31 May 2026'],
    ['2026-09-01T12:00:00Z', '1 Sep 2026'],
  ])('writes %s with a three-letter month', (time, expected) => {
    expect(dateText(time, 'UTC')).toBe(expected)
  })
})

describe('gameRowText', () => {
  it('names the computer by its strength and the colour the player has', () => {
    const game = unfinishedGame({
      playerSide: 'black',
      opponent: { kind: 'computer', strength: 1200 },
    })

    expect(gameRowText(game)).toMatchObject({
      opponent: 'Maia 1200',
      colour: 'Black',
    })
  })

  it.each([1, 3])(
    'marks a game with %i hinted positions as one in which a hint was used',
    hintCount => {
      expect(gameRowText(unfinishedGame({ hintCount })).hint).toBe('Hint used')
    }
  )

  it('has no hint marker for a game without a hint', () => {
    expect(gameRowText(unfinishedGame({ hintCount: 0 })).hint).toBeUndefined()
  })

  it('names the opponent "You" in a game the player moves both sides of', () => {
    const game = unfinishedGame({ opponent: { kind: 'none', strength: null } })

    expect(gameRowText(game).opponent).toBe('You')
  })
})
