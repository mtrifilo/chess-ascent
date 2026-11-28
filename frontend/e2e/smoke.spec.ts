import { expect, test, type Locator } from '@playwright/test'

// The whole application, once through: sign up, play a move against the
// computer and see its reply, resign, and find the game in My games. The
// screens are driven by the names and labels they show, as a person would
// read them. The API's computer plays the first legal move in ordinal order
// of its coordinates (playwright.config.ts), so its reply to 1.e4 is a7-a5.

// A new account each run; the password is made up and meets the API's rules.
const email = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 10)}@example.com`
const password = 'Smoke-test-1' // gitleaks:allow (made up for this test)

test('a new player plays the computer, resigns and finds the game in My games', async ({
  page,
}) => {
  await page.goto('/sign-up')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign up' }).click()

  await expect(page.getByRole('heading', { name: 'New game' })).toBeVisible()
  await page
    .getByRole('group', { name: 'Play as' })
    .getByRole('radio', { name: 'White' })
    .check()
  await page
    .getByRole('group', { name: 'Opponent strength' })
    .getByRole('radio', { name: '800' })
    .check()
  await page.getByRole('button', { name: 'Start game' }).click()

  await expect(page.getByRole('heading', { name: 'Your move' })).toBeVisible()
  const gamePath = new URL(page.url()).pathname
  expect(gamePath).toMatch(/^\/games\/[0-9a-f-]{36}$/)

  const board = page.getByRole('img', { name: 'Chess board' })
  await clickSquare(board, 'e2')
  await clickSquare(board, 'e4')

  // The reply shows once the computer has stopped thinking, which lasts at
  // least a set time; the expectations wait for it rather than for a time.
  const moves = page
    .getByRole('table')
    .filter({ has: page.getByRole('columnheader', { name: 'Black' }) })
  await expect(
    moves.getByRole('row', { name: '1. e4 a5', exact: true })
  ).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Your move' })).toBeVisible()
  await expect.poll(() => pieceOn(board, 'e4')).toBe('white pawn')
  await expect.poll(() => pieceOn(board, 'a5')).toBe('black pawn')
  await expect.poll(() => pieceOn(board, 'a7')).toBeUndefined()

  await page.getByRole('button', { name: 'Resign' }).click()
  await page
    .getByRole('group', { name: 'Resign this game? This cannot be undone.' })
    .getByRole('button', { name: 'Resign' })
    .click()
  await expect(
    page.getByRole('heading', { name: 'Maia 800 won' })
  ).toBeVisible()
  await expect(page.getByText('You resigned.')).toBeVisible()

  await page.getByRole('link', { name: 'My games' }).click()
  const finished = page.getByRole('table', { name: 'Finished' })
  const row = finished.getByRole('row').filter({ hasText: 'Lost, resigned' })
  await expect(row).toHaveCount(1)
  await expect(row).toContainText('Maia 800')
  await expect(row).toContainText('White')
  await expect(row.getByRole('link', { name: 'Review' })).toHaveAttribute(
    'href',
    gamePath
  )
})

// The board is drawn for sight alone (chess-board.tsx names it as one
// image), so a square is found where a person sees it: White is at the
// bottom, files a to h run left to right, and each square is an eighth of
// the board.

type Square =
  `${'a' | 'b' | 'c' | 'd' | 'e' | 'f' | 'g' | 'h'}${1 | 2 | 3 | 4 | 5 | 6 | 7 | 8}`

async function clickSquare(board: Locator, square: Square) {
  const box = await boundingBox(board)
  const { x, y } = centreOf(square, box)
  await board.page().mouse.click(x, y)
}

// The piece drawn on `square`, such as "black pawn", or undefined for an
// empty square. Chessground draws each piece as an element whose classes
// are its colour and kind.
async function pieceOn(
  board: Locator,
  square: Square
): Promise<string | undefined> {
  const box = await boundingBox(board)
  const { x, y } = centreOf(square, box)
  const pieces = await board.locator('piece').evaluateAll(elements =>
    elements.map(element => {
      const rect = element.getBoundingClientRect()
      return {
        name: element.className,
        left: rect.left,
        top: rect.top,
        right: rect.right,
        bottom: rect.bottom,
      }
    })
  )
  return pieces.find(
    piece =>
      piece.left <= x && x < piece.right && piece.top <= y && y < piece.bottom
  )?.name
}

type Box = { x: number; y: number; width: number; height: number }

async function boundingBox(board: Locator): Promise<Box> {
  const box = await board.boundingBox()
  if (!box) throw new Error('The board is not on the page.')
  return box
}

function centreOf(square: Square, box: Box) {
  const file = square.charCodeAt(0) - 'a'.charCodeAt(0)
  const rank = Number(square[1])
  return {
    x: box.x + ((file + 0.5) * box.width) / 8,
    y: box.y + ((8 - rank + 0.5) * box.height) / 8,
  }
}
