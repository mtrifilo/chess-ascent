import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { renderWithQueries } from '@/lib/test-support'

import GamesPage from './games/page'
import PlayPage from './play/page'

vi.mock('next/navigation', () => ({ useRouter: vi.fn() }))

describe('placeholder pages', () => {
  it.each([
    ['/play', PlayPage, 'Play'],
    ['/games', GamesPage, 'My games'],
  ])('%s shows its heading', (_route, Page, heading) => {
    renderWithQueries(<Page />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(heading)
  })

  it('/play offers a new game', () => {
    renderWithQueries(<PlayPage />)

    expect(screen.getByRole('button', { name: 'New game' })).toBeEnabled()
  })
})
