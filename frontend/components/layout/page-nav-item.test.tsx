import { render, screen } from '@testing-library/react'
import { usePathname } from 'next/navigation'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ActiveNavLinks } from './active-nav-links'
import type { NavHref } from './nav-links'
import { PageNavItemProvider, useNavItemForPage } from './page-nav-item'

vi.mock('next/navigation', () => ({ usePathname: vi.fn() }))

beforeEach(() => {
  vi.mocked(usePathname).mockReturnValue('/games/42')
})

function Page({ item }: { item?: NavHref }) {
  useNavItemForPage(item)
  return null
}

// The top bar's links and a page, under one provider. `rerenderWith` swaps
// the page and keeps the provider.
function renderWith(page: React.ReactNode) {
  const tree = (current: React.ReactNode) => (
    <PageNavItemProvider>
      <ActiveNavLinks />
      {current}
    </PageNavItemProvider>
  )
  const { rerender } = render(tree(page))
  return { rerenderWith: (next: React.ReactNode) => rerender(tree(next)) }
}

const current = () =>
  screen
    .queryAllByRole('link')
    .filter(link => link.getAttribute('aria-current') === 'page')
    .map(link => link.textContent)

describe('a page that names its navigation item', () => {
  it('has that item highlighted, on a path no item matches', () => {
    renderWith(<Page item="/play" />)

    expect(current()).toEqual(['Play'])
  })

  it('changes the highlight when it names another item', () => {
    const { rerenderWith } = renderWith(<Page item="/play" />)

    rerenderWith(<Page item="/games" />)

    expect(current()).toEqual(['My games'])
  })

  it('leaves the highlight to the path once it is gone', () => {
    const { rerenderWith } = renderWith(<Page item="/play" />)

    rerenderWith(null)

    expect(current()).toEqual([])
  })

  it('wins over the item the path matches', () => {
    vi.mocked(usePathname).mockReturnValue('/play')
    renderWith(<Page item="/games" />)

    expect(current()).toEqual(['My games'])
  })
})
