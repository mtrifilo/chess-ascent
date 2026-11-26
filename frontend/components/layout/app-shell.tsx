import { Suspense } from 'react'

import { AccountMenu } from './account-menu'
import { ActiveNavLinks } from './active-nav-links'
import { NavLinks } from './nav-links'

type AppShellProps = {
  // The signed-in user's email; absent when nobody is signed in.
  email?: string
  children: React.ReactNode
}

// The top bar and the page area below it. Signed out, the bar shows only the
// product name. The bar's 1px top padding balances its 1px bottom border, so
// its items centre on the full 56px as drawn. The spacer sits before the
// links on phones, pushing them right with the account menu, and after them
// from `md` up, where the links stay beside the product name.
export function AppShell({ email, children }: AppShellProps) {
  return (
    <>
      <header className="flex h-14 items-center gap-1 border-b bg-background pt-px pr-2 pl-4 md:gap-6 md:px-6">
        <span className="text-product-name font-semibold whitespace-nowrap">
          Chess Ascent
        </span>
        {email && (
          <>
            <div aria-hidden className="flex-1 md:hidden" />
            {/* The current path is unknown while a route with a dynamic
                segment, such as one game's page, is prerendered, and Cache
                Components fails the build if it is read outside Suspense.
                The fallback is the same links with none highlighted. */}
            <Suspense fallback={<NavLinks />}>
              <ActiveNavLinks />
            </Suspense>
            <div aria-hidden className="hidden flex-1 md:block" />
            <AccountMenu email={email} />
          </>
        )}
      </header>
      <main className="p-4 md:p-8">{children}</main>
    </>
  )
}
