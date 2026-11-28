import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { Activity } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  answer,
  calls,
  type Reply,
  renderWithQueries,
  type Sent,
} from '@/lib/test-support'

import { dateText } from './game-row-text'
import { MyGamesScreen } from './my-games-screen'
import { finishedGame, page, unfinishedGame } from './test-game-lists'

vi.mock('@/lib/page-load', () => ({ loadPage: vi.fn(), reloadPage: vi.fn() }))

const GAMES_PATH = '/api/games'
const ONGOING = `GET ${GAMES_PATH}?status=ongoing`
const FINISHED = `GET ${GAMES_PATH}?status=finished`

const ok = (body: unknown): Reply => ({ status: 200, body })
const problem = (status: number): Reply => ({ status, body: { status } })

// Answers the list API by status, and by cursor for later pages.
function lists(replies: {
  ongoing?: Reply | Promise<Reply>
  finished?: Reply | Promise<Reply>
  pages?: Record<string, Reply>
}) {
  return answer({
    [GAMES_PATH]: request => {
      const query = new URL(request.url).searchParams
      const cursor = query.get('cursor')
      if (cursor) return replies.pages?.[cursor] ?? problem(400)
      const status = query.get('status') as 'ongoing' | 'finished'
      return replies[status] ?? ok(page([]))
    },
  })
}

// An IntersectionObserver that reports what the test says is near the
// window.
type Observed = { target: Element; report: IntersectionObserverCallback }
const observed = new Set<Observed>()

class TestIntersectionObserver {
  private readonly entries: Observed[] = []
  constructor(private readonly callback: IntersectionObserverCallback) {}
  observe(target: Element) {
    const entry = { target, report: this.callback }
    this.entries.push(entry)
    observed.add(entry)
  }
  unobserve() {}
  disconnect() {
    for (const entry of this.entries) observed.delete(entry)
  }
  takeRecords() {
    return []
  }
}

// Reports every observed element inside `container` as near the window, as
// when the reader scrolls to the end of that list.
function scrollToEndOf(container: HTMLElement) {
  act(() => {
    for (const { target, report } of [...observed]) {
      if (!container.contains(target)) continue
      report(
        [{ target, isIntersecting: true } as IntersectionObserverEntry],
        {} as IntersectionObserver
      )
    }
  })
}

beforeEach(() => {
  observed.clear()
  vi.stubGlobal('IntersectionObserver', TestIntersectionObserver)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const table = (name: string) => screen.getByRole('table', { name })
const phoneList = (name: string) => screen.getByRole('list', { name })
const section = (name: string) =>
  screen.getByRole('heading', { level: 2, name }).parentElement!
const listHeadings = () =>
  screen.getAllByRole('heading', { level: 2 }).map(h => h.textContent)

// Each body row of a table, as the text of its cells.
function tableRows(name: string) {
  return within(table(name))
    .getAllByRole('row')
    .slice(1)
    .map(row =>
      within(row)
        .getAllByRole('cell')
        .map(cell => cell.textContent)
    )
}

// Each row of a phone list, as the text of its two lines and its link.
function phoneRows(name: string) {
  return within(phoneList(name))
    .getAllByRole('listitem')
    .map(item => [
      ...[...item.querySelectorAll('p')].map(line => line.textContent),
      within(item).getByRole('link').textContent,
    ])
}

describe('MyGamesScreen', () => {
  it('lists unfinished games, then finished ones, each from its own request', async () => {
    const sent = lists({
      ongoing: ok(page([unfinishedGame()])),
      finished: ok(page([finishedGame()])),
    })
    renderWithQueries(<MyGamesScreen />)

    await screen.findByRole('table', { name: 'Unfinished' })
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'My games'
    )
    expect(listHeadings()).toEqual(['Unfinished', 'Finished'])
    expect(calls(sent).sort()).toEqual([FINISHED, ONGOING])
  })

  it('heads the desktop table with the drawn columns', async () => {
    lists({ ongoing: ok(page([unfinishedGame()])) })
    renderWithQueries(<MyGamesScreen />)

    const unfinished = await screen.findByRole('table', { name: 'Unfinished' })
    expect(
      within(unfinished)
        .getAllByRole('columnheader')
        .map(head => head.textContent)
    ).toEqual(['Opponent', 'Your colour', 'Result', 'Date'])
  })

  it('shows each row the same way in the table and in the phone list', async () => {
    const ongoing = unfinishedGame({ halfMoveCount: 10 })
    const lost = finishedGame({
      playerSide: 'black',
      opponent: { kind: 'computer', strength: 1200 },
      result: 'whiteWins',
      endReason: 'resignation',
    })
    lists({
      ongoing: ok(page([ongoing])),
      finished: ok(page([lost])),
    })
    renderWithQueries(<MyGamesScreen />)

    await screen.findByRole('table', { name: 'Unfinished' })
    const ongoingDate = dateText(ongoing.lastActivityAt)
    const lostDate = dateText(lost.lastActivityAt)
    expect(tableRows('Unfinished')).toEqual([
      ['Maia 800', 'White', 'In progress, move 6', ongoingDate, 'Resume'],
    ])
    expect(tableRows('Finished')).toEqual([
      ['Maia 1200', 'Black', 'Lost, resigned', lostDate, 'Review'],
    ])
    expect(phoneRows('Unfinished')).toEqual([
      ['Maia 800 White', `In progress, move 6 ${ongoingDate}`, 'Resume'],
    ])
    expect(phoneRows('Finished')).toEqual([
      ['Maia 1200 Black', `Lost, resigned ${lostDate}`, 'Review'],
    ])
  })

  it('opens each game at its own address from a link', async () => {
    const ongoing = unfinishedGame()
    const finished = finishedGame()
    lists({ ongoing: ok(page([ongoing])), finished: ok(page([finished])) })
    renderWithQueries(<MyGamesScreen />)

    const unfinished = await screen.findByRole('table', { name: 'Unfinished' })
    expect(
      within(unfinished).getByRole('link', { name: 'Resume' })
    ).toHaveAttribute('href', `/games/${ongoing.id}`)
    expect(
      within(table('Finished')).getByRole('link', { name: 'Review' })
    ).toHaveAttribute('href', `/games/${finished.id}`)
    expect(
      within(phoneList('Unfinished')).getByRole('link', { name: 'Resume' })
    ).toHaveAttribute('href', `/games/${ongoing.id}`)
    expect(
      within(phoneList('Finished')).getByRole('link', { name: 'Review' })
    ).toHaveAttribute('href', `/games/${finished.id}`)
  })

  it('shows the empty state when the player has no games', async () => {
    lists({})
    renderWithQueries(<MyGamesScreen />)

    expect(await screen.findByText('No games yet.')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Games you start appear here, so you can resume or review them.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'New game' })).toHaveAttribute(
      'href',
      '/play'
    )
    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it.each([
    ['unfinished', { finished: ok(page([finishedGame()])) }, 'Finished'],
    ['finished', { ongoing: ok(page([unfinishedGame()])) }, 'Unfinished'],
  ])(
    'leaves out the %s list, heading and all, when it has no games',
    async (_empty, replies, shown) => {
      lists(replies)
      renderWithQueries(<MyGamesScreen />)

      await screen.findByRole('table', { name: shown })
      expect(listHeadings()).toEqual([shown])
      expect(screen.queryByText('No games yet.')).not.toBeInTheDocument()
    }
  )

  it('shows nothing below the heading until both lists have answered', async () => {
    let settle!: (reply: Reply) => void
    const sent = lists({
      ongoing: ok(page([unfinishedGame()])),
      finished: new Promise<Reply>(resolve => (settle = resolve)),
    })
    renderWithQueries(<MyGamesScreen />)

    await waitFor(() => expect(calls(sent)).toContain(FINISHED))
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'My games'
    )
    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument()
    expect(screen.queryByText('No games yet.')).not.toBeInTheDocument()

    settle(ok(page([])))
    expect(
      await screen.findByRole('table', { name: 'Unfinished' })
    ).toBeInTheDocument()
  })

  describe('when a list fails', () => {
    beforeEach(() => {
      vi.spyOn(console, 'error').mockImplementation(() => {})
    })

    it('leaves that list out, shows the other, and reports why', async () => {
      lists({
        ongoing: problem(500),
        finished: ok(page([finishedGame()])),
      })
      renderWithQueries(<MyGamesScreen />)

      await screen.findByRole('table', { name: 'Finished' })
      expect(listHeadings()).toEqual(['Finished'])
      expect(console.error).toHaveBeenCalledWith(
        new Error('GET /games answered 500.')
      )
    })

    it('does not show the empty state', async () => {
      lists({ ongoing: problem(500) })
      renderWithQueries(<MyGamesScreen />)

      await waitFor(() => expect(console.error).toHaveBeenCalled())
      expect(screen.queryByText('No games yet.')).not.toBeInTheDocument()
      expect(
        screen.queryByRole('heading', { level: 2 })
      ).not.toBeInTheDocument()
    })
  })

  describe('with more games than one page', () => {
    const first = unfinishedGame()
    const second = unfinishedGame()

    function twoPages(next: Reply = ok(page([second]))) {
      return lists({
        ongoing: ok(page([first], 'page-2')),
        pages: { 'page-2': next },
      })
    }

    const pageTwoCalls = (sent: Sent[]) =>
      calls(sent).filter(call => call.includes('cursor=page-2'))

    const unfinishedLinks = () =>
      within(table('Unfinished'))
        .getAllByRole('link')
        .map(link => link.getAttribute('href'))

    it('loads the next page when the reader scrolls near the end', async () => {
      const sent = twoPages()
      renderWithQueries(<MyGamesScreen />)
      await screen.findByRole('table', { name: 'Unfinished' })
      expect(unfinishedLinks()).toEqual([`/games/${first.id}`])

      scrollToEndOf(section('Unfinished'))

      await waitFor(() =>
        expect(unfinishedLinks()).toEqual([
          `/games/${first.id}`,
          `/games/${second.id}`,
        ])
      )
      expect(calls(sent)).toContain(
        `GET ${GAMES_PATH}?status=ongoing&cursor=page-2`
      )
    })

    it('loads the next page when the keyboard reaches the last row', async () => {
      twoPages()
      renderWithQueries(<MyGamesScreen />)
      await screen.findByRole('table', { name: 'Unfinished' })

      const lastLink = within(table('Unfinished')).getByRole('link')
      lastLink.focus()
      fireEvent.focus(lastLink)

      await waitFor(() => expect(unfinishedLinks()).toHaveLength(2))
      expect(lastLink).toHaveFocus()
    })

    it('asks for each page once, and no more after the last', async () => {
      const sent = twoPages()
      renderWithQueries(<MyGamesScreen />)
      await screen.findByRole('table', { name: 'Unfinished' })

      scrollToEndOf(section('Unfinished'))
      scrollToEndOf(section('Unfinished'))
      await waitFor(() => expect(unfinishedLinks()).toHaveLength(2))
      scrollToEndOf(section('Unfinished'))

      expect(pageTwoCalls(sent)).toHaveLength(1)
    })

    it('keeps the rows it has, and stops, when the next page fails', async () => {
      vi.spyOn(console, 'error').mockImplementation(() => {})
      const sent = twoPages(problem(500))
      renderWithQueries(<MyGamesScreen />)
      await screen.findByRole('table', { name: 'Unfinished' })

      scrollToEndOf(section('Unfinished'))
      await waitFor(() => expect(console.error).toHaveBeenCalled())
      scrollToEndOf(section('Unfinished'))

      expect(unfinishedLinks()).toEqual([`/games/${first.id}`])
      expect(pageTwoCalls(sent)).toHaveLength(1)
    })
  })

  it('reads the lists afresh, from the top, each time it is shown again', async () => {
    const before = unfinishedGame({ halfMoveCount: 4 })
    const after = { ...before, halfMoveCount: 6 }
    const screenIn = (mode: 'visible' | 'hidden') => (
      <Activity mode={mode}>
        <MyGamesScreen />
      </Activity>
    )
    lists({ ongoing: ok(page([before])) })
    const { rerender } = renderWithQueries(screenIn('visible'))
    await screen.findByRole('table', { name: 'Unfinished' })
    expect(tableRows('Unfinished')[0][2]).toBe('In progress, move 3')

    // The player leaves, the game gains a move elsewhere, and they return.
    rerender(screenIn('hidden'))
    let settle!: (reply: Reply) => void
    const sent = lists({
      ongoing: new Promise<Reply>(resolve => (settle = resolve)),
    })
    rerender(screenIn('visible'))

    await waitFor(() => expect(calls(sent)).toContain(ONGOING))
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    settle(ok(page([after])))
    await waitFor(() =>
      expect(tableRows('Unfinished')[0][2]).toBe('In progress, move 4')
    )
  })
})
