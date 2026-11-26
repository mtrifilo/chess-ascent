import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import GamesPage from './games/page'
import PlayPage from './play/page'

describe('placeholder pages', () => {
  it.each([
    ['/play', PlayPage, 'Play'],
    ['/games', GamesPage, 'My games'],
  ])('%s shows its heading', (_route, Page, heading) => {
    render(<Page />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(heading)
  })
})
