import Link from 'next/link'

import { cn } from '@/lib/utils'

export const NAV_ITEMS = [
  { href: '/play', label: 'Play' },
  { href: '/games', label: 'My games' },
] as const

export type NavHref = (typeof NAV_ITEMS)[number]['href']

// The item whose page is showing. Only an exact path matches, so nothing is
// highlighted on a path below an item, such as one game's page. The designs
// highlight Play on a game being played and My games on one being replayed,
// which the path does not tell apart, so such a page names its item itself
// (page-nav-item.tsx).
export function activeNavHref(pathname: string): NavHref | undefined {
  return NAV_ITEMS.find(item => item.href === pathname)?.href
}

export function NavLinks({ activeHref }: { activeHref?: NavHref }) {
  return (
    <nav>
      <ul className="flex items-center gap-1">
        {NAV_ITEMS.map(item => {
          const isActive = item.href === activeHref
          return (
            <li key={item.href}>
              <Link
                href={item.href}
                aria-current={isActive ? 'page' : undefined}
                className={cn(
                  'flex h-11 items-center rounded-md px-3 text-sm whitespace-nowrap focus-visible:outline-hidden focus-visible:ring-[3px] focus-visible:ring-ring md:h-8',
                  isActive
                    ? // Forced-colours mode drops the fill, so the current
                      // page keeps a cue there as an underline.
                      'bg-accent text-accent-foreground forced-colors:underline'
                    : 'text-muted-foreground'
                )}
              >
                {item.label}
              </Link>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
