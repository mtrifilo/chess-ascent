'use client'

import { useInfiniteQuery } from '@tanstack/react-query'
import { useLayoutEffect, useState } from 'react'

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
// idle until the first visit starts. A page the API does not answer with
// games is a failure, reported by the query cache.
function useGameList(status: GameProgress, visit: number) {
  return useInfiniteQuery({
    queryKey: ['games', status, visit],
    enabled: visit > 0,
    gcTime: 0,
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

// Counts the times the calling component has been shown: 0 before it first
// is. React runs layout effects again each time hidden content is shown, and
// a state set in one renders again before the browser paints, so a new
// visit never shows the last one's lists even for a frame.
//
// The lint rule below warns that a state set in an effect renders again at
// once. Here that second render, before the paint, is the purpose: it is
// what React documents useLayoutEffect for.
function useVisit(): number {
  const [visit, setVisit] = useState(0)
  useLayoutEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setVisit(count => count + 1)
  }, [])
  return visit
}
