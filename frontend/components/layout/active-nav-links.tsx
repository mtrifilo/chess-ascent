'use client'

import { usePathname } from 'next/navigation'

import { activeNavHref, NavLinks } from './nav-links'
import { usePageNavItem } from './page-nav-item'

// The item the page has named wins over the one its path matches.
export function ActiveNavLinks() {
  const named = usePageNavItem()
  const matched = activeNavHref(usePathname())
  return <NavLinks activeHref={named ?? matched} />
}
