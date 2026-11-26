import Link from 'next/link'

import { cn } from '@/lib/utils'

export const NAV_ITEMS = [
  { href: '/play', label: 'Play' },
  { href: '/games', label: 'My games' },
] as const

export type NavHref = (typeof NAV_ITEMS)[number]['href']

// The item whose page is showing. Only an exact path matches: a game at
// /games/[id] is drawn with Play highlighted while it is played and My games
// while it is replayed, which the path alone cannot tell apart.
export function activeNavHref(pathname: string): NavHref | undefined {
  return NAV_ITEMS.find(item => item.href === pathname)?.href
}

export function NavLinks({ activeHref }: { activeHref?: NavHref }) {
  return (
    <nav className="ml-auto md:ml-0">
      <ul className="flex items-center gap-1">
        {NAV_ITEMS.map(item => {
          const isActive = item.href === activeHref
          return (
            <li key={item.href}>
              <Link
                href={item.href}
                aria-current={isActive ? 'page' : undefined}
                className={cn(
                  'flex h-11 items-center rounded-md px-3 text-sm whitespace-nowrap md:h-8',
                  isActive
                    ? 'bg-accent text-accent-foreground'
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
