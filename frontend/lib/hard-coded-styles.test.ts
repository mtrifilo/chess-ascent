import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

// Colours and font sizes live in app/globals.css only. Tailwind's stock
// palette is removed there, so `text-white` does not compile; these patterns
// catch the ways around that which do compile: arbitrary values in class
// names and literal colours or sizes in inline styles.
const FORBIDDEN = [
  { name: 'arbitrary colour', pattern: /-\[(?:color:)?#[0-9a-f]{3,8}\]/i },
  {
    name: 'colour function',
    pattern: /\b(?:rgba?|hsla?|oklch|oklab|lab|lch|color-mix)\(/i,
  },
  {
    name: 'arbitrary font size',
    pattern: /\btext-\[(?:length:)?[\d.]+(?:px|r?em|pt)\]/,
  },
  { name: 'inline font size', pattern: /\bfontSize\s*:/ },
  { name: 'inline colour', pattern: /style=\{\{[^}]*\b(?:color|background)/ },
]

const FRONTEND = join(__dirname, '..')
const SOURCE_DIRS = ['app', 'components', 'features', 'lib']

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    const isSource = /\.(ts|tsx)$/.test(entry.name)
    const isTest = /\.test\.(ts|tsx)$/.test(entry.name)
    return isSource && !isTest ? [path] : []
  })
}

describe('styles outside the token file', () => {
  const files = SOURCE_DIRS.flatMap(dir => sourceFiles(join(FRONTEND, dir)))

  it('finds the source files to check', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  it('hard-codes no colour or font size', () => {
    const findings = files.flatMap(file => {
      const text = readFileSync(file, 'utf8')
      return FORBIDDEN.filter(rule => rule.pattern.test(text)).map(
        rule => `${relative(FRONTEND, file)}: ${rule.name}`
      )
    })

    expect(findings).toEqual([])
  })
})
