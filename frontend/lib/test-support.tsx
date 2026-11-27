import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import type { useRouter } from 'next/navigation'
import type { Middleware } from 'openapi-fetch'
import { afterEach } from 'vitest'

import { api } from '@/lib/api'

// Helpers for component tests that call the API. Imported only by tests.

export type Reply = { status: number; body?: unknown }

// A request the client sent: "METHOD /path", and its JSON body.
export type Sent = { call: string; body: unknown }

export const SIGNED_OUT: Reply = { status: 401, body: { status: 401 } }

export function signedInAs(email: string): Reply {
  return { status: 200, body: { email } }
}

let answerMiddleware: Middleware | undefined

// Answers each request from `replies`, by path, instead of sending it:
// openapi-fetch hands every request to its middleware first, and a Response
// returned there is used as the server's answer. Each call replaces the
// previous replies. Returns the requests sent from then on.
export function answer(replies: Record<string, Reply>): Sent[] {
  if (answerMiddleware) api.eject(answerMiddleware)
  const sent: Sent[] = []
  answerMiddleware = {
    onRequest: async ({ request }) => {
      const path = new URL(request.url).pathname
      const text = await request.clone().text()
      sent.push({
        call: `${request.method} ${path}`,
        body: text ? JSON.parse(text) : undefined,
      })
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
  return sent
}

export function calls(sent: Sent[]) {
  return sent.map(request => request.call)
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

// A stand-in for useRouter's result, for a test that mocks next/navigation.
export function routerWith(replace: () => void) {
  return { replace } as unknown as ReturnType<typeof useRouter>
}

// Types an email and a password and presses the form's submit button.
export function fillIn(email: string, password: string, submit: string) {
  fireEvent.change(screen.getByLabelText('Email'), {
    target: { value: email },
  })
  fireEvent.change(screen.getByLabelText('Password'), {
    target: { value: password },
  })
  fireEvent.click(screen.getByRole('button', { name: submit }))
}
