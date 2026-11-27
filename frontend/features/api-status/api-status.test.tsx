import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { paths } from '@/types/api'

import { ApiStatus } from './api-status'

type Answer = { status: number; body: string; contentType: string } | Error

// The component's requests are answered here, by path, rather than by a
// server. Only the transport is replaced: the client is openapi-fetch typed by
// the generated paths, as in lib/api.ts, so the component's handling of what
// it parses is what is tested.
const transport = vi.hoisted(() => ({
  answers: {} as Record<string, Answer>,
  requested: [] as string[],
  // Node's Request, which openapi-fetch builds every call on, rejects a
  // relative URL, which a browser resolves against the page. This resolves
  // it against jsdom's location in the same way.
  Request: class extends Request {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(
        typeof input === 'string' ? new URL(input, location.href) : input,
        init
      )
    }
  },
}))

vi.mock('@/lib/api', async importOriginal => {
  const { default: createClient } = await import('openapi-fetch')
  return {
    ...(await importOriginal<typeof import('@/lib/api')>()),
    api: createClient<paths>({
      baseUrl: '/api',
      Request: transport.Request,
      fetch: async request => {
        const path = new URL(request.url).pathname
        transport.requested.push(path)
        const reply = transport.answers[path]
        if (!reply) throw new Error(`unexpected request to ${path}`)
        if (reply instanceof Error) throw reply
        return new Response(reply.body, {
          status: reply.status,
          headers: { 'Content-Type': reply.contentType },
        })
      },
    }),
  }
})

function answer(answers: Record<string, Answer>) {
  transport.answers = answers
  transport.requested = []
}

const version = (value: string): Answer => ({
  status: 200,
  body: JSON.stringify({ version: value, commit: null }),
  contentType: 'application/json',
})

const readiness = (status: number, body: string): Answer => ({
  status,
  body,
  contentType: 'text/plain',
})

function valueOf(label: string) {
  return screen.getByText(label, { selector: 'dt' }).nextElementSibling
}

describe('ApiStatus', () => {
  it('shows the readiness and the version the API answers with', async () => {
    answer({
      '/api/health/ready': readiness(200, 'Healthy'),
      '/api/version': version('0.1.0'),
    })

    render(<ApiStatus />)

    expect(await screen.findByText('Healthy')).toBeInTheDocument()
    expect(await screen.findByText('0.1.0')).toBeInTheDocument()
    expect(valueOf('API')).toHaveTextContent('Healthy')
    expect(valueOf('Version')).toHaveTextContent('0.1.0')
  })

  it('shows Unhealthy when readiness answers 503', async () => {
    answer({
      '/api/health/ready': readiness(503, 'Unhealthy'),
      '/api/version': version('0.1.0'),
    })

    render(<ApiStatus />)

    expect(await screen.findByText('Unhealthy')).toBeInTheDocument()
  })

  it('leaves both values blank when the API cannot be reached', async () => {
    answer({
      '/api/health/ready': readiness(500, 'Internal Server Error'),
      '/api/version': new TypeError('fetch failed'),
    })

    render(<ApiStatus />)

    await vi.waitFor(() => expect(transport.requested).toHaveLength(2))
    // Let both answers settle before checking nothing was shown.
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(valueOf('API')).toBeEmptyDOMElement()
    expect(valueOf('Version')).toBeEmptyDOMElement()
  })
})
