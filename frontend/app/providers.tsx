'use client'

import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'

import { createQueryClient } from '@/lib/query-client'

// One query cache for the whole app in the browser, so every component that
// reads server state, such as who is signed in, shares one answer. A server
// render gets a cache of its own each time, so nothing cached for one
// request is seen by another. Nothing is fetched during a server render:
// queries start in the browser.
let browserQueryClient: QueryClient | undefined

function getQueryClient() {
  if (typeof window === 'undefined') return createQueryClient()
  browserQueryClient ??= createQueryClient()
  return browserQueryClient
}

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <QueryClientProvider client={getQueryClient()}>
      {children}
    </QueryClientProvider>
  )
}
