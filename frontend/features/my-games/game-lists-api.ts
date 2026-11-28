'use client'

import { useInfiniteQuery } from '@tanstack/react-query'
import { useLayoutEffect, useRef, useState } from 'react'

import { api } from '@/lib/api'
import type { components } from '@/types/api'

type GameProgress = components['schemas']['GameProgress']

// The signed-in player's two lists of games, unfinished and finished, each
// read from the top as the screen is shown.
//
// Next.js keeps a page the visitor has left in the document, hidden, and
// shows it again on return as it was (see its guide "How Next.js preserves
// UI state with Activity"). Each showing of the screen is therefore a visit
// of its own, with lists of its own: on return the lists are read afresh
// from their first page, as they are now, and nothing from the last visit
// is shown meanwhile. Each visit's lists are dropped from the cache as soon
// as nothing shows them.
export function useGameLists() {
  const visit = useVisit()
  return {
    unfinished: useGameList('ongoing', visit),
    finished: useGameList('finished', visit),
  }
}

export type GameListQuery = ReturnType<typeof useGameList>

// One list, most recent activity first, a page at a time. The query is
// idle outside a visit. A page the API does not answer with games is a
// failure, reported by the query cache.
//
// A failed page is not tried again. The screen shows nothing for a list that
// failed, so trying again would only hold back the other list, which waits
// for this one so as to appear below it, by the seconds TanStack Query's
// default retries take. The list is read again on the next visit, and when
// the window regains focus or the browser comes back online.
function useGameList(status: GameProgress, visit: number) {
  return useInfiniteQuery({
    queryKey: ['games', status, visit],
    enabled: visit > 0,
    gcTime: 0,
    retry: false,
    initialPageParam: null as string | null,
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/games', {
        params: { query: { status, cursor: pageParam ?? undefined } },
        signal,
      })
      if (!data) throw new Error(`GET /games answered ${response.status}.`)
      return data
    },
    getNextPageParam: page => page.nextCursor,
  })
}

// The number of the current showing of the calling component, counting
// from 1, or 0 while it is not shown. React runs layout effects again each
// time hidden content is shown, and a state set in one renders again before
// the browser paints, so a new visit never shows the last one's lists even
// for a frame.
//
// Hiding sets it back to 0, so the render that shows the component again has
// no visit: its lists are idle, and the last visit's are not read again for
// the moment before the new visit starts.
function useVisit(): number {
  const visits = useRef(0)
  const [visit, setVisit] = useState(0)
  useLayoutEffect(() => {
    visits.current += 1
    setVisit(visits.current)
    return () => setVisit(0)
  }, [])
  return visit
}
