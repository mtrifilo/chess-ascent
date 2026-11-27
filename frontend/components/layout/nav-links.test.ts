import { describe, expect, it } from 'vitest'

import { activeNavHref } from './nav-links'

describe('activeNavHref', () => {
  it('matches a navigation item by exact path', () => {
    expect(activeNavHref('/play')).toBe('/play')
    expect(activeNavHref('/games')).toBe('/games')
  })

  it('matches nothing below a navigation item, such as one game', () => {
    expect(activeNavHref('/games/123')).toBeUndefined()
    expect(activeNavHref('/')).toBeUndefined()
  })
})
