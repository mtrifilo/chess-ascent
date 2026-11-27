import { render, screen } from '@testing-library/react'
import type { Middleware } from 'openapi-fetch'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { api } from '@/lib/api'

import { ApiStatus } from './api-status'

type Answer = { status: number; body: string; contentType: string } | Error

// The real client answers each request from here, by path, instead of
// sending it: openapi-fetch hands every request to its middleware first, and
// a Response returned there is used as the server's answer.
let answerMiddleware: Middleware | undefined
const requested: string[] = []

function answer(answers: Record<string, Answer>) {
  answerMiddleware = {
    onRequest: ({ request }) => {
      const path = new URL(request.url).pathname
      requested.push(path)
      const reply = answers[path]
      if (!reply) throw new Error(`unexpected request to ${path}`)
      if (reply instanceof Error) throw reply
      return new Response(reply.body, {
        status: reply.status,
        headers: { 'Content-Type': reply.contentType },
      })
    },
  }
  api.use(answerMiddleware)
}

afterEach(() => {
  if (answerMiddleware) api.eject(answerMiddleware)
  answerMiddleware = undefined
  requested.length = 0
})

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

    await vi.waitFor(() => {
      expect(valueOf('API')).toHaveTextContent('Healthy')
      expect(valueOf('Version')).toHaveTextContent('0.1.0')
    })
  })

  it('shows Unhealthy when readiness answers 503', async () => {
    answer({
      '/api/health/ready': readiness(503, 'Unhealthy'),
      '/api/version': version('0.1.0'),
    })

    render(<ApiStatus />)

    await vi.waitFor(() =>
      expect(valueOf('API')).toHaveTextContent('Unhealthy')
    )
  })

  it.each([
    [
      'a 503 with a problem-details body',
      {
        status: 503,
        body: JSON.stringify({ title: 'Service Unavailable', status: 503 }),
        contentType: 'application/problem+json',
      },
    ],
    [
      'a 200 with a body the API does not document',
      {
        status: 200,
        body: '<html>other server</html>',
        contentType: 'text/html',
      },
    ],
    ['a 200 that says Unhealthy', readiness(200, 'Unhealthy')],
  ])('leaves the readiness blank for %s', async (_case, reply) => {
    answer({
      '/api/health/ready': reply,
      '/api/version': version('0.1.0'),
    })

    render(<ApiStatus />)

    await vi.waitFor(() =>
      expect(valueOf('Version')).toHaveTextContent('0.1.0')
    )
    await vi.waitFor(() => expect(requested).toHaveLength(2))
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(valueOf('API')).toBeEmptyDOMElement()
  })

  it('leaves both values blank when the API cannot be reached', async () => {
    answer({
      '/api/health/ready': readiness(500, 'Internal Server Error'),
      '/api/version': new TypeError('fetch failed'),
    })

    render(<ApiStatus />)

    await vi.waitFor(() => expect(requested).toHaveLength(2))
    // Let both answers settle before checking nothing was shown.
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(valueOf('API')).toBeEmptyDOMElement()
    expect(valueOf('Version')).toBeEmptyDOMElement()
  })
})
