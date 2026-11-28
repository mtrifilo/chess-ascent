import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { renderWithQueries } from '@/lib/test-support'

import GamesPage from './games/page'

describe('placeholder pages', () => {
  it('/games shows its heading', () => {
    renderWithQueries(<GamesPage />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'My games'
    )
  })
})
