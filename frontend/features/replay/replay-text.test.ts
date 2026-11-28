import { describe, expect, it } from 'vitest'

import { computerGame, game } from '@/features/game/test-games'

import { replaySummary } from './replay-text'

// Tests run in Los Angeles time (vitest.config.mts), where 18:30 UTC on
// 25 November is the same day.
const finished = (overrides: Parameters<typeof computerGame>[0]) =>
  computerGame({
    status: 'finished',
    lastActivityAt: '2026-11-25T18:30:00+00:00',
    ...overrides,
  })

describe('replaySummary', () => {
  it('words a win by checkmate as drawn', () => {
    const won = finished({ result: 'whiteWins', endReason: 'checkmate' })

    expect(replaySummary(won)).toBe(
      'You won by checkmate against Maia 800 · 25 Nov 2026'
    )
  })

  it.each([
    ['white', 'blackWins', 'checkmate', 'You lost by checkmate'],
    ['black', 'blackWins', 'checkmate', 'You won by checkmate'],
    ['black', 'whiteWins', 'checkmate', 'You lost by checkmate'],
    ['white', 'blackWins', 'resignation', 'You resigned'],
    ['white', 'draw', 'stalemate', 'Draw by stalemate'],
    ['white', 'draw', 'threefoldRepetition', 'Draw by threefold repetition'],
    ['white', 'draw', 'fiftyMoveRule', 'Draw by fifty-move rule'],
  ] as const)(
    'words a game the player had %s in, with %s by %s, as "%s"',
    (playerSide, result, endReason, ending) => {
      const game = finished({ playerSide, result, endReason })

      expect(replaySummary(game)).toBe(
        `${ending} against Maia 800 · 25 Nov 2026`
      )
    }
  )

  it('puts the opponent before the reason of a draw in a dead position', () => {
    const game = finished({ result: 'draw', endReason: 'deadPosition' })

    expect(replaySummary(game)).toBe(
      'Draw against Maia 800, neither side can checkmate · 25 Nov 2026'
    )
  })

  it('names the opponent by its strength', () => {
    const game = finished({
      result: 'whiteWins',
      endReason: 'checkmate',
      opponent: { kind: 'computer', strength: 1400 },
    })

    expect(replaySummary(game)).toContain('against Maia 1400')
  })

  it('names no opponent in a game the player moves both sides of', () => {
    const both = game({
      status: 'finished',
      result: 'whiteWins',
      endReason: 'checkmate',
      lastActivityAt: '2026-11-25T18:30:00+00:00',
    })

    expect(replaySummary(both)).toBe('You won by checkmate · 25 Nov 2026')
  })

  it("dates the game by its last activity in the viewer's time zone", () => {
    // 03:00 on 26 November in UTC is still 25 November in Los Angeles.
    const late = finished({
      result: 'whiteWins',
      endReason: 'checkmate',
      lastActivityAt: '2026-11-26T03:00:00+00:00',
    })

    expect(replaySummary(late)).toMatch(/· 25 Nov 2026$/)
  })
})
