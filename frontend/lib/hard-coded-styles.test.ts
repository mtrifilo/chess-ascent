import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

// Colours, font sizes, radii and shadows live in app/globals.css. This test
// catches the common ways around that: literal colours in class names, inline
// styles, SVG and icon attributes and other stylesheets; arbitrary font sizes;
// the stock Tailwind utilities globals.css removes, which then generate no CSS
// and no error; and `rounded-full`, which still compiles but is banned (no
// pill shapes). A token reached through `var(...)` passes. It is a pattern
// check, not a parser: a named colour inside a multi-part arbitrary value
// (`shadow-[0_0_0_1px_red]`) or a colour held in a variable gets past it.
// Comments are not checked.
type Rule = { name: string; pattern: RegExp; cssOnly?: boolean }

const COLOUR_UTILITY =
  'bg|text|border(?:-[trblxyse])?|ring|ring-offset|outline|fill|stroke|decoration|divide|from|via|to|shadow|accent|caret|placeholder'
const STOCK_PALETTE =
  'white|black|slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose'
const ALLOWED_COLOUR_WORDS = 'currentcolor|transparent|inherit|none'
// A value that is a token or a colour-neutral keyword, after an opening quote.
// Spelled out in both cases because the rule using it is case-sensitive.
const ALLOWED_VALUE = `(?:(?:currentColor|currentcolor|transparent|inherit|none)["'\`]|var\\(|url\\()`

const RULES: Rule[] = [
  {
    // `_` may precede it, as Tailwind's arbitrary values use it for spaces.
    // A `#` after `href="`, `.` or a letter is an anchor, a private field or
    // a word, not a colour.
    name: 'hex colour',
    pattern:
      /(?<![a-z0-9&.]|href=["'])#(?:[0-9a-f]{8}|[0-9a-f]{6}|[0-9a-f]{3,4})(?![\w-])/gi,
  },
  {
    // Not `\b`: `_` is a word character, so `shadow-[0_0_rgb(...)]` has no
    // word boundary before rgb.
    name: 'colour function',
    pattern: /(?<![a-z])(?:rgba?|hsla?|hwb|oklch|oklab|lab|lch|color-mix)\(/gi,
  },
  {
    name: 'arbitrary named colour',
    pattern: new RegExp(
      `(?<![\\w-])(?:${COLOUR_UTILITY})-\\[(?:color:)?(?!(?:${ALLOWED_COLOUR_WORDS})\\])[a-z]+\\]`,
      'gi'
    ),
  },
  {
    name: 'arbitrary colour or font-size property',
    pattern:
      /\[(?:color|background(?:-color)?|border(?:-[a-z]+)?-color|outline-color|fill|stroke|box-shadow|font-size)\s*:(?!\s*var\()/gi,
  },
  {
    // On HTML and SVG elements (lower-case tags) and icons only: a
    // component's own `color` prop, such as a chess piece's side, is not a
    // colour. Case-sensitive, so `<Piece` is not read as an element.
    name: 'colour attribute',
    pattern: new RegExp(
      `<(?:[a-z][\\w-]*|\\w*Icon)\\b[^>]*?\\s(?:color|fill|stroke|stop-color|stopColor)=(?:"|'|\\{\\s*["'\`])(?!${ALLOWED_VALUE})`,
      'g'
    ),
  },
  {
    name: 'arbitrary font size',
    pattern:
      /(?<![\w-])text-\[(?:length:)?(?:[\d.]|calc\(|clamp\(|min\(|max\()/g,
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
    // Bare `rounded` and `rounded-t` need the removed default radius. An
    // object key such as `shadow:` is not a class.
    name: 'stock or pill radius',
    pattern:
      /(?<![\w-])rounded(?:-[trblse]{1,2})?(?:-(?:xs|2xl|3xl|4xl|full))?(?![\w-]|\s*:)/g,
  },
  {
    name: 'stock shadow',
    pattern:
      /(?<![\w-])(?:drop-|inset-|text-)?shadow(?:-(?:2xs|xs|sm|md|lg|xl|2xl))?(?![\w-]|\s*:)/g,
  },
  {
    name: 'stylesheet colour or font size',
    cssOnly: true,
    pattern:
      /(?<![\w-])(?:color|background(?:-color)?|border(?:-[a-z]+)?-color|outline-color|fill|stroke|box-shadow|font-size|font)\s*:(?!\s*var\()/gi,
  },
]

// Inside an inline style object, a colour, shadow or font property set to a
// literal string or number rather than a `var(...)` token. Checked across
// lines, as Prettier splits objects.
const INLINE_STYLE = /style=\{\{([\s\S]*?)\}\}/g
const INLINE_STYLE_VALUE =
  /\b(?:\w*[cC]olor|background|\w*[sS]hadow|fill|stroke|font|fontSize)\s*:\s*(?:["'`](?!var\()|\d)/g

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

// Blanks out comments, keeping line breaks so line numbers still hold. A `//`
// inside a string, such as a URL, blanks the rest of that line too, which
// only means less is checked there.
function withoutComments(text: string): string {
  const blank = (comment: string) => comment.replace(/[^\n]/g, ' ')
  return text.replace(/\/\*[\s\S]*?\*\//g, blank).replace(/\/\/[^\n]*/g, blank)
}

function lineOf(text: string, index: number): number {
  return text.slice(0, index).split('\n').length
}

function findings(file: string, source: string): string[] {
  const text = withoutComments(source)
  const isCss = file.endsWith('.css')
  const found = RULES.filter(rule => isCss || !rule.cssOnly).flatMap(rule =>
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
    '[outline-color:red]',
    '[font-size:13px]',
    '[--x:#f00]',
    'shadow-[0_0_0_1px_#d2541b]',
    'shadow-[0_0_0_rgb(0,0,0)]',
    'text-[13px]',
    'text-[calc(1rem+1px)]',
    'text-[clamp(12px,2vw,16px)]',
    'text-white',
    'bg-zinc-500',
    'bg-black/50',
    'text-lg',
    'rounded',
    'rounded-t',
    'rounded-full',
    'rounded-2xl',
    'shadow',
    'shadow-md',
    'drop-shadow-md',
    'inset-shadow-sm',
    'text-shadow-lg',
    '<UserIcon color="#d2541b" />',
    '<UserIcon color="red" />',
    '<path fill="red" stroke="black" />',
    '<stop stopColor="red" />',
    'style={{ fontSize: 13 }}',
    'style={{ color: "red" }}',
    "style={{ borderColor: '#f00' }}",
    "style={{ boxShadow: '0 0 0 2px red' }}",
    "style={{ font: '13px serif' }}",
    "style={{\n  padding: 0,\n  color: 'red',\n}}",
  ])('rejects %s', sample => {
    expect(findings('sample.tsx', sample)).not.toEqual([])
  })

  it.each(['.x { color: red }', '.x { font-size: 13px }'])(
    'rejects %s in a stylesheet',
    sample => {
      expect(findings('sample.css', sample)).not.toEqual([])
    }
  )

  it.each([
    'bg-accent',
    'text-body-s',
    'text-sm',
    'md:text-sm',
    'rounded-md',
    'rounded-sm',
    'shadow-popover',
    'border-current',
    'bg-[var(--board-hint)]',
    'bg-board-square-light',
    '[color:var(--link)]',
    'ring-[3px]',
    'border-[currentColor]',
    'grid-cols-[0_1fr]',
    'min-w-[8rem]',
    'has-[>svg]:px-3',
    'href="#main"',
    'href="#add"',
    'this.#cafe()',
    '// see issue #123',
    '/* the #d2541b primary */',
    '<path fill="none" stroke="currentColor" />',
    '<path fill="var(--piece-white)" stroke="var(--piece-black)" />',
    "<path fill={'var(--ring)'} />",
    '<path fill="url(#grad)" />',
    '<Piece color="white" type="k" />',
    "<Clock color={'black'} />",
    "style={{ color: 'var(--link)' }}",
    "style={{ backgroundColor: isLight ? 'var(--board-square-light)' : 'var(--board-square-dark)' }}",
    'style={{ backgroundImage: `url(${src})` }}',
    "style={{ fontVariantNumeric: 'tabular-nums' }}",
    'style={{ fontWeight: 600 }}',
    'style={{ width: 4 }}',
    'data-[variant=destructive]:text-destructive',
  ])('accepts %s', sample => {
    expect(findings('sample.tsx', sample)).toEqual([])
  })
})
