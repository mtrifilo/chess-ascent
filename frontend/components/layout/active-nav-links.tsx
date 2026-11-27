'use client'

import { usePathname } from 'next/navigation'

import { activeNavHref, NavLinks } from './nav-links'

export function ActiveNavLinks() {
  return <NavLinks activeHref={activeNavHref(usePathname())} />
}
