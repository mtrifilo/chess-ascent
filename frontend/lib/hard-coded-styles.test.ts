import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

// Colours and font sizes live in app/globals.css only. Tailwind's stock
// palette is removed there, so `text-white` does not compile; these patterns
// catch the ways around that which do compile: arbitrary values in class
// names and literal colours or sizes in inline styles. A token reached
// through `var(...)` passes.
const COLOUR_UTILITY =
  '(?:bg|text|border(?:-[trblxy])?|ring|ring-offset|outline|fill|stroke|decoration|divide|from|via|to|shadow|accent|caret|placeholder)'
const COLOUR_WORDS_ALLOWED = '(?:currentcolor|transparent|inherit)'

const FORBIDDEN = [
  {
    name: 'arbitrary hex colour',
    pattern: /-\[(?:color:)?#[0-9a-f]{3,8}\]/i,
  },
  {
    name: 'arbitrary named colour',
    pattern: new RegExp(
      `\\b${COLOUR_UTILITY}-\\[(?:color:)?(?!${COLOUR_WORDS_ALLOWED}\\])[a-z]+\\]`,
      'i'
    ),
  },
  {
    name: 'arbitrary colour property',
    pattern:
      /\[(?:color|background(?:-color)?|border(?:-[a-z]+)?-color|fill|stroke)\s*:(?!\s*var\()/i,
  },
  {
    name: 'colour function',
    // Not `\b`: Tailwind writes spaces in arbitrary values as `_`, a word
    // character, so `shadow-[0_0_rgb(...)]` has no word boundary before rgb.
    pattern: /(?<![a-z])(?:rgba?|hsla?|oklch|oklab|lab|lch|color-mix)\(/i,
  },
  {
    name: 'arbitrary font size',
    pattern: /\btext-\[(?:length:)?(?:[\d.]|calc\()/,
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

function findings(file: string, text: string): string[] {
  return text
    .split('\n')
    .flatMap((line, index) =>
      FORBIDDEN.filter(rule => rule.pattern.test(line)).map(
        rule => `${file}:${index + 1}: ${rule.name}`
      )
    )
}

describe('styles outside the token file', () => {
  const files = SOURCE_DIRS.flatMap(dir => sourceFiles(join(FRONTEND, dir)))

  it('finds the source files to check', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  it('hard-codes no colour or font size', () => {
    const found = files.flatMap(file =>
      findings(relative(FRONTEND, file), readFileSync(file, 'utf8'))
    )

    expect(found).toEqual([])
  })

  it.each([
    'bg-[#fff]',
    'text-[red]',
    'border-t-[color:white]',
    '[color:red]',
    'shadow-[0_0_0_rgb(0,0,0)]',
    'text-[13px]',
    'text-[calc(1rem+1px)]',
    'style={{ fontSize: 13 }}',
    'style={{ color: "red" }}',
  ])('rejects %s', sample => {
    expect(findings('sample', sample)).not.toEqual([])
  })

  it.each([
    'bg-accent',
    'text-body-s',
    'border-current',
    'bg-[var(--board-hint)]',
    '[color:var(--link)]',
    'ring-[3px]',
    'border-[currentColor]',
    'grid-cols-[0_1fr]',
    'min-w-[8rem]',
    'has-[>svg]:px-3',
  ])('accepts %s', sample => {
    expect(findings('sample', sample)).toEqual([])
  })
})
