import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import Home from './page'

// The API status calls the API; its own tests cover it.
vi.mock('@/features/api-status/api-status', () => ({ ApiStatus: () => null }))

describe('Home', () => {
  it('names the product in its top-level heading', () => {
    render(<Home />)

    const heading = screen.getByRole('heading', { level: 1 })

    expect(heading.textContent).toBe('Chess Ascent')
  })
})
