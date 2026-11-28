'use client'

import { useCallback, useEffect, useRef } from 'react'

// What a list paged with TanStack Query's useInfiniteQuery tells about its
// next page.
type PagedList = {
  data: { pages: readonly unknown[] } | undefined
  hasNextPage: boolean
  isFetching: boolean
  isFetchNextPageError: boolean
  fetchNextPage: (options: { cancelRefetch: boolean }) => Promise<unknown>
}

// How far below the window a list's end is when its next page is asked for:
// one window's height, so the rows are usually there before the reader
// scrolls to them.
const LOOK_AHEAD = '0px 0px 100% 0px'

// The one place a list asks for its next page, with no control and no
// wording: when the list's end comes near the window, as the reader scrolls,
// and when the keyboard reaches the list's last row, which a screen reader's
// browsing also scrolls into view. Rows are added below the ones shown, so
// nothing the reader is looking at moves. A button that asks for the next
// page could replace this hook.
//
// `endRef` goes on an element just after the list's last row, and
// `onLastRowFocus` on whatever in that row takes the keyboard's focus.
//
// One page is asked for at a time, and none while the list is being read
// afresh. Both signals can come at once, as when the keyboard scrolls the
// last row into view, and the second comes before the screen knows of the
// first: `cancelRefetch: false` makes a request already on its way answer
// it, where TanStack Query would otherwise cancel that request and send it
// again. A page that fails is not asked for again until the list has been
// read afresh: the rows already shown stay, and the query cache reports the
// failure.
export function useNextPage({
  data,
  hasNextPage,
  isFetching,
  isFetchNextPageError,
  fetchNextPage,
}: PagedList) {
  const canLoad = hasNextPage && !isFetching && !isFetchNextPageError
  const loadNextPage = useCallback(() => {
    if (canLoad) void fetchNextPage({ cancelRefetch: false })
  }, [canLoad, fetchNextPage])

  // A new observer after each page reports at once whether the end is still
  // near, as it is when a page does not fill the window, so pages keep
  // coming until it is not. The count of pages, not only `canLoad`, starts
  // one: a page that arrives quickly can be shown in the same render that
  // would have shown its request, and `canLoad` is then true throughout.
  const pageCount = data?.pages.length ?? 0
  const endRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const end = endRef.current
    if (!end || !canLoad) return
    const observer = new IntersectionObserver(
      entries => {
        if (entries.some(entry => entry.isIntersecting)) loadNextPage()
      },
      { rootMargin: LOOK_AHEAD }
    )
    observer.observe(end)
    return () => observer.disconnect()
  }, [canLoad, loadNextPage, pageCount])

  return { endRef, onLastRowFocus: loadNextPage }
}
