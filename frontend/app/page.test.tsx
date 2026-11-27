import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import Home from './page'

// The API status calls the API, and its own tests cover what it shows. Here
// it is replaced by a marker, so this test sends no requests and still sees
// whether the page renders it.
vi.mock('@/features/api-status/api-status', () => ({
  ApiStatus: () => <div data-testid="api-status" />,
}))

describe('Home', () => {
  it('names the product in its top-level heading', () => {
    render(<Home />)

    const heading = screen.getByRole('heading', { level: 1 })

    expect(heading.textContent).toBe('Chess Ascent')
  })

  it('shows the API status', () => {
    render(<Home />)

    expect(screen.getByTestId('api-status')).toBeInTheDocument()
  })
})
