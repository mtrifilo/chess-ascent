'use client'

import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'

import { PageNavItemProvider } from '@/components/layout/page-nav-item'
import { createQueryClient } from '@/lib/query-client'

// One query cache for the whole app in the browser, so every component that
// reads server state, such as who is signed in, shares one answer. A server
// render gets a cache of its own each time, so nothing cached for one
// request is seen by another. Nothing is fetched during a server render:
// queries start in the browser.
//
// The page's navigation item is shared the same way, between the top bar,
// which highlights it, and a page that names it.
let browserQueryClient: QueryClient | undefined

function getQueryClient() {
  if (typeof window === 'undefined') return createQueryClient()
  browserQueryClient ??= createQueryClient()
  return browserQueryClient
}

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <QueryClientProvider client={getQueryClient()}>
      <PageNavItemProvider>{children}</PageNavItemProvider>
    </QueryClientProvider>
  )
}
