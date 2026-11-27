import { screen, waitFor } from '@testing-library/react'
import { useRouter } from 'next/navigation'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { reloadPage } from '@/lib/page-load'
import {
  answer,
  renderWithQueries,
  routerWith,
  SIGNED_OUT,
  signedInAs,
} from '@/lib/test-support'

import { RequireAccount } from './require-account'

vi.mock('next/navigation', () => ({ useRouter: vi.fn() }))
vi.mock('@/lib/page-load', () => ({ reloadPage: vi.fn(), loadPage: vi.fn() }))

const replace = vi.fn()

beforeEach(() => {
  replace.mockReset()
  vi.mocked(reloadPage).mockReset()
  vi.mocked(useRouter).mockReturnValue(routerWith(replace))
})

afterEach(() => {
  window.history.replaceState(null, '', '/')
})

describe('RequireAccount', () => {
  it('shows the page to a signed-in visitor', async () => {
    answer({
      '/api/auth/me': signedInAs('a@example.com'),
    })
    renderWithQueries(<RequireAccount>My games</RequireAccount>)

    expect(await screen.findByText('My games')).toBeInTheDocument()
    expect(replace).not.toHaveBeenCalled()
  })

  it('sends a signed-out visitor to sign in, carrying the page they wanted', async () => {
    window.history.replaceState(null, '', '/games/42')
    answer({ '/api/auth/me': SIGNED_OUT })
    renderWithQueries(<RequireAccount>My games</RequireAccount>)

    await waitFor(() =>
      expect(replace).toHaveBeenCalledWith('/sign-in?next=%2Fgames%2F42')
    )
    expect(screen.queryByText('My games')).not.toBeInTheDocument()
    expect(reloadPage).not.toHaveBeenCalled()
  })

  it('loads the page afresh when a session it showed has ended', async () => {
    answer({ '/api/auth/me': signedInAs('a@example.com') })
    const { queryClient } = renderWithQueries(
      <RequireAccount>My games</RequireAccount>
    )
    await screen.findByText('My games')

    answer({ '/api/auth/me': SIGNED_OUT })
    await queryClient.refetchQueries()

    expect(reloadPage).toHaveBeenCalledOnce()
  })

  it('shows nothing until the API says who is signed in', () => {
    answer({
      '/api/auth/me': signedInAs('a@example.com'),
    })
    renderWithQueries(<RequireAccount>My games</RequireAccount>)

    expect(screen.queryByText('My games')).not.toBeInTheDocument()
  })
})
