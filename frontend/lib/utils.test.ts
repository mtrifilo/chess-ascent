import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

import { cn } from './utils'

// The theme keys globals.css adds in the `--text-*` and `--shadow-*`
// namespaces, without the `--text-x--line-height` style modifiers.
function addedThemeKeys(namespace: 'text' | 'shadow'): string[] {
  const css = readFileSync(join(__dirname, '..', 'app', 'globals.css'), 'utf8')
  const keys = [
    ...css.matchAll(new RegExp(`--${namespace}-([a-z0-9-]+):`, 'g')),
  ]
    .map(match => match[1])
    .filter(key => !key.includes('--'))
  return [...new Set(keys)]
}

describe('cn', () => {
  it('finds the added theme keys to check', () => {
    expect(addedThemeKeys('text')).toContain('body-s')
    expect(addedThemeKeys('shadow')).toContain('popover')
  })

  it.each(addedThemeKeys('text'))(
    'keeps text-%s beside a text colour',
    size => {
      expect(cn(`text-${size}`, 'text-muted-foreground')).toBe(
        `text-${size} text-muted-foreground`
      )
    }
  )

  it.each(addedThemeKeys('shadow'))(
    'lets a later shadow replace shadow-%s',
    shadow => {
      expect(cn(`shadow-${shadow}`, 'shadow-none')).toBe('shadow-none')
    }
  )
})
