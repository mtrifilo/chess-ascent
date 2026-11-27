import { Suspense } from 'react'

import { SessionControls } from './session-controls'

// The top bar and the page area below it. Signed out, and until the browser
// learns who is signed in, the bar shows only the product name.
//
// The controls that depend on the session sit in their own Suspense slot:
// should reading the session ever suspend, only they wait, never the page.
//
// The bar's 1px top padding balances its 1px bottom border, so its items
// centre on the full 56px as drawn. account-menu.tsx derives the menu's
// offset from this height: change the two together.
export function AppShell({ children }: { children: React.ReactNode }) {
  return (
    <>
      <header className="flex h-14 items-center gap-1 border-b bg-background pt-px pr-2 pl-4 md:gap-6 md:px-6">
        <span className="shrink-0 text-product-name font-semibold whitespace-nowrap">
          Chess Ascent
        </span>
        <Suspense>
          <SessionControls />
        </Suspense>
      </header>
      <main className="p-4 md:p-8">{children}</main>
    </>
  )
}
