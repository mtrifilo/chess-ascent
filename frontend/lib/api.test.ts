import { afterEach, describe, expect, it, vi } from 'vitest'

import { signInOnUnauthorized } from './api'
import { loadPage } from './page-load'

vi.mock('./page-load', () => ({ loadPage: vi.fn() }))

// The middleware is called directly: requests answered by a test's own
// middleware skip every onResponse, so it cannot be reached through the
// client in jsdom.
function respond(schemaPath: string, status: number) {
  signInOnUnauthorized.onResponse?.({
    schemaPath,
    response: new Response(null, { status }),
  } as Parameters<NonNullable<typeof signInOnUnauthorized.onResponse>>[0])
}

afterEach(() => {
  vi.mocked(loadPage).mockReset()
  window.history.replaceState(null, '', '/')
})

describe('signInOnUnauthorized', () => {
  it('loads the sign-in page, returning to this page, on a 401', () => {
    window.history.replaceState(null, '', '/games/42?step=3')

    respond('/games/{id}', 401)

    expect(loadPage).toHaveBeenCalledWith(
      '/sign-in?next=%2Fgames%2F42%3Fstep%3D3'
    )
  })

  it.each([
    [
      'the current-user check, which is how a signed-out visitor is told',
      '/auth/me',
    ],
    ['sign-in, whose 401 is a wrong email or password', '/auth/sign-in'],
  ])('leaves a 401 from %s to its caller', (_case, schemaPath) => {
    respond(schemaPath, 401)

    expect(loadPage).not.toHaveBeenCalled()
  })

  it.each([200, 400, 403, 500])('does nothing on a %i', status => {
    respond('/games', status)

    expect(loadPage).not.toHaveBeenCalled()
  })
})
