import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

// Colours, font sizes, radii and shadows live in app/globals.css only. These
// rules catch the ways around it: literal colours anywhere (class names,
// inline styles, SVG and icon attributes, other stylesheets), arbitrary font
// sizes, and the stock Tailwind utilities globals.css removes, which would
// otherwise generate no CSS without any error. A token reached through
// `var(...)` passes.
type Rule = { name: string; pattern: RegExp }

const COLOUR_UTILITY =
  'bg|text|border(?:-[trblxyse])?|ring|ring-offset|outline|fill|stroke|decoration|divide|from|via|to|shadow|accent|caret|placeholder'
const STOCK_PALETTE =
  'white|black|slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose'
const ALLOWED_COLOUR_WORDS = 'currentcolor|transparent|inherit|none'

const RULES: Rule[] = [
  {
    name: 'hex colour',
    // `_` may precede it: Tailwind's arbitrary values use it for spaces.
    pattern:
      /(?<![a-z0-9&])#(?:[0-9a-f]{8}|[0-9a-f]{6}|[0-9a-f]{3,4})(?![\w-])/gi,
  },
  {
    // Not `\b`: Tailwind writes spaces in arbitrary values as `_`, a word
    // character, so `shadow-[0_0_rgb(...)]` has no word boundary before rgb.
    name: 'colour function',
    pattern: /(?<![a-z])(?:rgba?|hsla?|oklch|oklab|lab|lch|color-mix)\(/gi,
  },
  {
    name: 'arbitrary named colour',
    pattern: new RegExp(
      `(?<![\\w-])(?:${COLOUR_UTILITY})-\\[(?:color:)?(?!(?:${ALLOWED_COLOUR_WORDS})\\])[a-z]+\\]`,
      'gi'
    ),
  },
  {
    name: 'arbitrary colour property',
    pattern:
      /\[(?:color|background(?:-color)?|border(?:-[a-z]+)?-color|fill|stroke|box-shadow)\s*:(?!\s*var\()/gi,
  },
  {
    name: 'colour attribute',
    pattern: new RegExp(
      `(?<![\\w-])(?:color|fill|stroke)=(?:"|'|\\{\\s*["'\`])(?!(?:${ALLOWED_COLOUR_WORDS}|var\\(|url\\()["'\`)])`,
      'gi'
    ),
  },
  {
    name: 'arbitrary font size',
    pattern: /(?<![\w-])text-\[(?:length:)?(?:[\d.]|calc\()/g,
  },
  {
    name: 'stock palette utility',
    pattern: new RegExp(
      `(?<![\\w-])(?:${COLOUR_UTILITY})-(?:${STOCK_PALETTE})(?:-\\d{2,3})?(?:/\\d+)?(?![\\w-])`,
      'g'
    ),
  },
  {
    name: 'stock text size',
    pattern: /(?<![\w-])text-(?:lg|xl|[2-9]xl)(?![\w-])/g,
  },
  {
    name: 'stock or pill radius',
    pattern:
      /(?<![\w-])rounded(?:-[trblse]{1,2})?-(?:xs|2xl|3xl|4xl|full)(?![\w-])/g,
  },
  {
    name: 'stock shadow',
    pattern: /(?<![\w-])shadow-(?:2xs|xs|sm|md|lg|xl|2xl)(?![\w-])/g,
  },
]

// Inside an inline style object, any colour, shadow or font value that is
// not a `var(...)` token. Checked across lines, as Prettier splits objects.
const INLINE_STYLE = /style=\{\{([\s\S]*?)\}\}/g
const INLINE_STYLE_VALUE =
  /\b(?:\w*color|background\w*|fill|stroke|\w*shadow|font\w*)\s*:(?!\s*["'`]?var\()/gi

const FRONTEND = join(__dirname, '..')
const SOURCE_DIRS = ['app', 'components', 'features', 'lib']
const TOKEN_FILE = join('app', 'globals.css')

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    const isSource = /\.(ts|tsx|css)$/.test(entry.name)
    const isTest = /\.test\.(ts|tsx)$/.test(entry.name)
    const isTokenFile = relative(FRONTEND, path) === TOKEN_FILE
    return isSource && !isTest && !isTokenFile ? [path] : []
  })
}

function lineOf(text: string, index: number): number {
  return text.slice(0, index).split('\n').length
}

function findings(file: string, text: string): string[] {
  const found = RULES.flatMap(rule =>
    [...text.matchAll(rule.pattern)].map(
      match => `${file}:${lineOf(text, match.index)}: ${rule.name}`
    )
  )
  for (const block of text.matchAll(INLINE_STYLE)) {
    for (const value of block[1].matchAll(INLINE_STYLE_VALUE)) {
      const index = block.index + block[0].indexOf(block[1]) + value.index
      found.push(`${file}:${lineOf(text, index)}: inline colour or font`)
    }
  }
  return found
}

describe('styles outside the token file', () => {
  const files = SOURCE_DIRS.flatMap(dir => sourceFiles(join(FRONTEND, dir)))

  it('finds the source files to check', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  it('hard-codes no colour, font size, radius or shadow', () => {
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
    '[--x:#f00]',
    'shadow-[0_0_0_1px_#d2541b]',
    'shadow-[0_0_0_rgb(0,0,0)]',
    'text-[13px]',
    'text-[calc(1rem+1px)]',
    'text-white',
    'bg-zinc-500',
    'bg-black/50',
    'text-lg',
    'rounded-full',
    'rounded-2xl',
    'shadow-md',
    '<UserIcon color="#d2541b" />',
    '<path fill="red" stroke="black" />',
    'style={{ fontSize: 13 }}',
    'style={{ color: "red" }}',
    "style={{ borderColor: '#f00' }}",
    "style={{ boxShadow: '0 0 0 2px red' }}",
    "style={{ font: '13px serif' }}",
    "style={{\n  padding: 0,\n  color: 'red',\n}}",
  ])('rejects %s', sample => {
    expect(findings('sample', sample)).not.toEqual([])
  })

  it.each([
    'bg-accent',
    'text-body-s',
    'text-sm',
    'rounded-md',
    'shadow-popover',
    'border-current',
    'bg-[var(--board-hint)]',
    '[color:var(--link)]',
    'ring-[3px]',
    'border-[currentColor]',
    'grid-cols-[0_1fr]',
    'min-w-[8rem]',
    'has-[>svg]:px-3',
    'href="#main"',
    '<path fill="none" stroke="currentColor" />',
    "style={{ color: 'var(--link)' }}",
    'style={{ width: 4 }}',
    'data-[variant=destructive]:text-destructive',
  ])('accepts %s', sample => {
    expect(findings('sample', sample)).toEqual([])
  })
})
