import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import Home from './page'

describe('Home', () => {
  it('names the product in its top-level heading', () => {
    render(<Home />)

    const heading = screen.getByRole('heading', { level: 1 })

    expect(heading.textContent).toBe('Chess Ascent')
  })
})
