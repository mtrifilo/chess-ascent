import { fireEvent, render, screen, within } from '@testing-library/react'
import { usePathname } from 'next/navigation'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AppShell } from './app-shell'

vi.mock('next/navigation', () => ({ usePathname: vi.fn() }))

const EMAIL = 'player@example.com'

beforeEach(() => {
  vi.mocked(usePathname).mockReturnValue('/play')
})

describe('AppShell', () => {
  it('shows only the product name when signed out', () => {
    render(<AppShell>Page</AppShell>)

    const banner = screen.getByRole('banner')
    expect(banner).toHaveTextContent(/^Chess Ascent$/)
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('renders the page inside the main landmark', () => {
    render(<AppShell>Page</AppShell>)

    expect(screen.getByRole('main')).toHaveTextContent('Page')
  })

  it('links to Play and My games when signed in', () => {
    render(<AppShell email={EMAIL}>Page</AppShell>)

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

  it('marks the link to the current page', () => {
    vi.mocked(usePathname).mockReturnValue('/games')
    render(<AppShell email={EMAIL}>Page</AppShell>)

    expect(screen.getByRole('link', { name: 'My games' })).toHaveAttribute(
      'aria-current',
      'page'
    )
    expect(screen.getByRole('link', { name: 'Play' })).not.toHaveAttribute(
      'aria-current'
    )
  })

  it('names the account menu trigger with the email at every width', () => {
    render(<AppShell email={EMAIL}>Page</AppShell>)

    expect(screen.getByRole('button', { name: EMAIL })).toHaveAttribute(
      'aria-haspopup',
      'menu'
    )
  })

  it('opens the account menu with the email and Sign out', () => {
    render(<AppShell email={EMAIL}>Page</AppShell>)

    fireEvent.keyDown(screen.getByRole('button', { name: EMAIL }), {
      key: 'Enter',
    })

    const menu = within(screen.getByRole('menu'))
    expect(menu.getByText(EMAIL)).toBeInTheDocument()
    expect(menu.getByRole('menuitem', { name: 'Sign out' })).toBeInTheDocument()
  })

  it('treats an empty email as signed out', () => {
    render(<AppShell email="">Page</AppShell>)

    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })
})
