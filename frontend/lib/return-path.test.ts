import { describe, expect, it } from 'vitest'

import { authPagePath, returnPathFrom } from './return-path'

describe('returnPathFrom', () => {
  it.each([
    ['/games', '/games'],
    ['/games/42', '/games/42'],
    ['/games?filter=finished#top', '/games?filter=finished#top'],
    ['/', '/'],
  ])('keeps the path within this site %s', (value, expected) => {
    expect(returnPathFrom(value)).toBe(expected)
  })

  it.each([
    ['nothing', null],
    ['an empty value', ''],
    ['another site', 'https://evil.example/phish'],
    ['another site without a scheme', '//evil.example/phish'],
    ['a backslash the browser reads as a slash', '/\\evil.example/phish'],
    ['two backslashes', '\\\\evil.example/phish'],
    ['a tab the URL parser drops', '/\t/evil.example/phish'],
    ['a newline the URL parser drops', '/\n/evil.example'],
    ['a script', 'javascript:alert(1)'],
    ['a relative path', 'games'],
    ['a data address', 'data:text/html,<p>hi</p>'],
  ])('refuses %s and goes to /play', (_case, value) => {
    expect(returnPathFrom(value)).toBe('/play')
  })

  it('keeps an encoded slash as part of the path, not a new host', () => {
    expect(returnPathFrom('/%2F%2Fevil.example')).toBe('/%2F%2Fevil.example')
  })
})

describe('authPagePath', () => {
  it('carries the return path in the query', () => {
    expect(authPagePath('/sign-in', '/games/42?x=1')).toBe(
      '/sign-in?next=%2Fgames%2F42%3Fx%3D1'
    )
  })

  it('is the bare page without one', () => {
    expect(authPagePath('/sign-up')).toBe('/sign-up')
  })
})
