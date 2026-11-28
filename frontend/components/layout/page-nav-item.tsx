'use client'

import { createContext, useContext, useEffect, useState } from 'react'

import type { NavHref } from './nav-links'

// The navigation item a page says it belongs to, for a page whose path alone
// does not say. One game's page is under Play while the game is being played
// and under My games once it is over, at the same path.

type PageNavItem = {
  href: NavHref | undefined
  setHref: (href: NavHref | undefined) => void
}

const PageNavItemContext = createContext<PageNavItem>({
  href: undefined,
  setHref: () => {},
})

export function PageNavItemProvider({
  children,
}: {
  children: React.ReactNode
}) {
  const [href, setHref] = useState<NavHref>()
  return (
    <PageNavItemContext value={{ href, setHref }}>
      {children}
    </PageNavItemContext>
  )
}

// The item the showing page has named, if any.
export function usePageNavItem(): NavHref | undefined {
  return useContext(PageNavItemContext).href
}

// Names the item to highlight while the calling page is showing. Leaving the
// page, or hiding it as Next.js does on navigation, clears the name again.
export function useNavItemForPage(href: NavHref | undefined) {
  const { setHref } = useContext(PageNavItemContext)
  useEffect(() => {
    setHref(href)
    return () => setHref(undefined)
  }, [href, setHref])
}
