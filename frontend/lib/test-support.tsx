import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { Middleware } from 'openapi-fetch'
import { afterEach } from 'vitest'

import { api } from '@/lib/api'

// Helpers for component tests that call the API. Imported only by tests.

export type Reply = { status: number; body?: unknown }

export type Sent = { call: string; body: unknown }

let answerMiddleware: Middleware | undefined

// Answers each request from `replies`, by path, instead of sending it:
// openapi-fetch hands every request to its middleware first, and a Response
// returned there is used as the server's answer. Each call replaces the
// previous replies. Returns the calls made, as "METHOD /path", and `sent`
// holds each one's JSON body.
export function answer(replies: Record<string, Reply>, sent: Sent[] = []) {
  if (answerMiddleware) api.eject(answerMiddleware)
  const requested: string[] = []
  answerMiddleware = {
    onRequest: async ({ request }) => {
      const path = new URL(request.url).pathname
      const call = `${request.method} ${path}`
      requested.push(call)
      const text = await request.clone().text()
      sent.push({ call, body: text ? JSON.parse(text) : undefined })
      const reply = replies[path]
      if (!reply) throw new Error(`unexpected request to ${path}`)
      return new Response(
        reply.body === undefined ? null : JSON.stringify(reply.body),
        {
          status: reply.status,
          headers: { 'Content-Type': 'application/json' },
        }
      )
    },
  }
  api.use(answerMiddleware)
  return requested
}

afterEach(() => {
  if (answerMiddleware) api.eject(answerMiddleware)
  answerMiddleware = undefined
})

// Renders with a query cache of its own, so no test sees another's session.
// Failed queries are not retried, so a test sees a failure at once.
export function renderWithQueries(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(ui, {
    wrapper: ({ children }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    ),
  })
}
