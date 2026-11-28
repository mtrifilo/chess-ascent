'use client'

import Link from 'next/link'
import { useId } from 'react'

import { PageHeading } from '@/components/layout/page-heading'
import { Button } from '@/components/ui/button'
import { ColourSwatch } from '@/features/game/colour-swatch'
import { HintUsed } from '@/features/game/hint-used'
import { NewGameLink } from '@/features/game/new-game-link'
import { cn } from '@/lib/utils'

import { useGameLists, type GameListQuery } from './game-lists-api'
import {
  gameRowText,
  type GameRowText,
  type GameSummary,
} from './game-row-text'
import { useNextPage } from './next-page'

// The signed-in player's games: the unfinished ones, then the finished ones,
// each in a list of its own that opens a game at /games/<id>. A table from
// `md` up, as drawn, and two-line rows on phones, from the same rows.
//
// A list with no games is left out, heading and all; with none in either,
// the screen says there are no games yet. Below the heading nothing shows
// until both lists have their first page or have failed, so one list never
// appears above the other after it, and nothing passes for the empty state
// before it is known. A list whose first page fails is left out, and the
// query cache reports why in the browser console. A list that fails when it
// is read again keeps what it showed: its rows, or its part in the empty
// state, which is decided from what the lists hold rather than from how
// their last read went.
export function MyGamesScreen() {
  const { unfinished, finished } = useGameLists()
  const settled = !unfinished.isPending && !finished.isPending
  const noGames =
    unfinished.data !== undefined &&
    finished.data !== undefined &&
    !hasGames(unfinished) &&
    !hasGames(finished)

  return (
    <div className="mx-auto flex w-full max-w-260 flex-col gap-6 md:gap-8">
      <PageHeading>My games</PageHeading>
      {settled &&
        (noGames ? (
          <NoGames />
        ) : (
          <>
            <GameList title="Unfinished" action="Resume" list={unfinished} />
            <GameList title="Finished" action="Review" list={finished} />
          </>
        ))}
    </div>
  )
}

function hasGames(list: GameListQuery): boolean {
  return list.data?.pages.some(page => page.games.length > 0) ?? false
}

type Row = { game: GameSummary; text: GameRowText }

const headClass =
  'px-2 py-2 text-left text-xs font-normal tracking-column-head text-muted-foreground uppercase'

function GameList({
  title,
  action,
  list,
}: {
  title: string
  action: string
  list: GameListQuery
}) {
  const headingId = useId()
  const { endRef, onLastRowFocus } = useNextPage(list)
  const rows: Row[] = (list.data?.pages ?? []).flatMap(page =>
    page.games.map(game => ({ game, text: gameRowText(game) }))
  )
  if (rows.length === 0) return null

  const isLast = (index: number) => index === rows.length - 1
  const open = (row: Row, index: number, className: string) => (
    <Button asChild variant="outline" className={className}>
      <Link
        href={`/games/${row.game.id}`}
        onFocus={isLast(index) ? onLastRowFocus : undefined}
      >
        {action}
      </Link>
    </Button>
  )

  return (
    <section className="flex flex-col gap-2">
      <h2 id={headingId} className="font-display text-display-s font-semibold">
        {title}
      </h2>
      <table
        aria-labelledby={headingId}
        className="hidden w-full table-fixed md:table"
      >
        <thead>
          <tr className="border-b">
            <th scope="col" className={cn(headClass, 'w-35')}>
              Opponent
            </th>
            <th scope="col" className={cn(headClass, 'w-32.5')}>
              Your colour
            </th>
            <th scope="col" className={headClass}>
              Result
            </th>
            <th scope="col" className={cn(headClass, 'w-32.5')}>
              Date
            </th>
            <th scope="col" className={cn(headClass, 'w-27.5')}>
              Hint
            </th>
            {/* The column of actions has no heading. */}
            <td className="w-26" />
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={row.game.id} className="h-12 border-b">
              <td className="px-2 text-sm">{row.text.opponent}</td>
              <td className="px-2 text-sm">
                <span className="flex items-center gap-2">
                  <ColourSwatch side={row.game.playerSide} />
                  {row.text.colour}
                </span>
              </td>
              <td className="px-2 text-sm">{row.text.result}</td>
              <td className="px-2 font-data text-data-m text-muted-foreground">
                {row.text.date}
              </td>
              <td className="px-2">
                {row.text.hint && <HintUsed>{row.text.hint}</HintUsed>}
              </td>
              <td className="px-2 text-right">
                {open(row, index, 'h-8 px-2')}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <ul aria-labelledby={headingId} className="md:hidden">
        {rows.map((row, index) => (
          <li
            key={row.game.id}
            className="flex items-center gap-3 border-b py-3"
          >
            {/* The spaces between the parts are for screen readers; the
                gaps between them are what shows. */}
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <p className="flex items-center gap-2 text-sm">
                <span className="font-semibold">{row.text.opponent}</span>{' '}
                <ColourSwatch side={row.game.playerSide} />
                <span>{row.text.colour}</span>
              </p>
              <p className="flex flex-wrap gap-x-2 text-body-s text-muted-foreground">
                <span>{row.text.result}</span>{' '}
                <span className="font-data">{row.text.date}</span>
              </p>
              {row.text.hint && (
                <HintUsed className="self-start">{row.text.hint}</HintUsed>
              )}
            </div>
            {open(row, index, 'h-12.75 px-4')}
          </li>
        ))}
      </ul>
      <div ref={endRef} />
    </section>
  )
}

// What a player with no games sees, as drawn: a card on every width, and the
// button the full width of it on phones.
function NoGames() {
  return (
    <div className="flex flex-col gap-3 rounded-md border p-4 md:p-6">
      <p className="text-sm">No games yet.</p>
      <p className="text-body-s text-muted-foreground">
        Games you start appear here, so you can resume or review them.
      </p>
      <NewGameLink />
    </div>
  )
}
