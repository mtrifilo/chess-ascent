import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { usePathname } from 'next/navigation'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { reloadPage } from '@/lib/page-load'
import { answer, renderWithQueries } from '@/lib/test-support'

import { AppShell } from './app-shell'

vi.mock('next/navigation', () => ({ usePathname: vi.fn() }))
vi.mock('@/lib/page-load', () => ({ reloadPage: vi.fn(), loadPage: vi.fn() }))

const EMAIL = 'player@example.com'

const signedIn = {
  '/api/auth/me': { status: 200, body: { email: EMAIL } },
}
const signedOut = {
  '/api/auth/me': { status: 401, body: { status: 401 } },
}

beforeEach(() => {
  vi.mocked(usePathname).mockReturnValue('/play')
  vi.mocked(reloadPage).mockReset()
})

async function renderSignedIn() {
  answer(signedIn)
  renderWithQueries(<AppShell>Page</AppShell>)
  return screen.findByRole('button', { name: EMAIL })
}

describe('AppShell', () => {
  it('shows only the product name when signed out', async () => {
    const requested = answer(signedOut)
    renderWithQueries(<AppShell>Page</AppShell>)

    await waitFor(() => expect(requested).toContain('GET /api/auth/me'))
    const banner = screen.getByRole('banner')
    expect(banner).toHaveTextContent(/^Chess Ascent$/)
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('renders the page inside the main landmark', () => {
    answer(signedOut)
    renderWithQueries(<AppShell>Page</AppShell>)

    expect(screen.getByRole('main')).toHaveTextContent('Page')
  })

  it('links to Play and My games when signed in', async () => {
    await renderSignedIn()

    const nav = within(screen.getByRole('navigation'))
    expect(nav.getByRole('link', { name: 'Play' })).toHaveAttribute(
      'href',
      '/play'
    )
    expect(nav.getByRole('link', { name: 'My games' })).toHaveAttribute(
      'href',
      '/games'
    )
  })

  it('marks the link to the current page', async () => {
    vi.mocked(usePathname).mockReturnValue('/games')
    await renderSignedIn()

    expect(screen.getByRole('link', { name: 'My games' })).toHaveAttribute(
      'aria-current',
      'page'
    )
    expect(screen.getByRole('link', { name: 'Play' })).not.toHaveAttribute(
      'aria-current'
    )
  })

  it('names the account menu trigger with the email at every width', async () => {
    const trigger = await renderSignedIn()

    expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
  })

  it('opens the account menu with the email and Sign out', async () => {
    const trigger = await renderSignedIn()

    fireEvent.keyDown(trigger, { key: 'Enter' })

    const menu = within(screen.getByRole('menu'))
    expect(menu.getByText(EMAIL)).toBeInTheDocument()
    expect(menu.getByRole('menuitem', { name: 'Sign out' })).toBeInTheDocument()
  })

  it('signs out from the account menu, then loads the page afresh', async () => {
    const trigger = await renderSignedIn()
    const requested = answer({
      ...signedIn,
      '/api/auth/sign-out': { status: 204 },
    })

    fireEvent.keyDown(trigger, { key: 'Enter' })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }))

    await waitFor(() => expect(reloadPage).toHaveBeenCalledOnce())
    expect(requested).toContain('POST /api/auth/sign-out')
  })

  it('stays signed in when the API refuses to sign out', async () => {
    const trigger = await renderSignedIn()
    const requested = answer({
      ...signedIn,
      '/api/auth/sign-out': { status: 403, body: { status: 403 } },
    })

    fireEvent.keyDown(trigger, { key: 'Enter' })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }))

    await waitFor(() => expect(requested).toContain('POST /api/auth/sign-out'))
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(reloadPage).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: EMAIL })).toBeInTheDocument()
  })
})
