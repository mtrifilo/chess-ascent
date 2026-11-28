import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { MoveList } from './move-list'
import { move } from './test-games'

const OPENING = [
  move(1, 'e2e4', 'e4'),
  move(2, 'e7e5', 'e5'),
  move(3, 'g1f3', 'Nf3'),
  move(4, 'b8c6', 'Nc6'),
  move(5, 'f1c4', 'Bc4'),
]

// The text of each body row's cells.
function rowsOf(table: HTMLElement) {
  const [, ...bodyRows] = within(table).getAllByRole('row')
  return bodyRows.map(row =>
    within(row)
      .getAllByRole('cell')
      .map(cell => cell.textContent)
  )
}

// The cells marked as the latest move.
const markedCells = () =>
  screen
    .getAllByRole('cell')
    .filter(cell => cell.getAttribute('aria-current') === 'step')

describe('MoveList', () => {
  it('heads its two columns White and Black', () => {
    render(<MoveList moves={OPENING} />)

    expect(
      screen.getAllByRole('columnheader').map(head => head.textContent)
    ).toEqual(['White', 'Black'])
  })

  it('shows the approved text before any move', () => {
    render(<MoveList moves={[]} />)

    expect(screen.getByText('No moves yet.')).toBeInTheDocument()
  })

  it('lists the moves in two columns with move numbers', () => {
    render(<MoveList moves={OPENING} />)

    expect(rowsOf(screen.getByRole('table'))).toEqual([
      ['1.', 'e4', 'e5'],
      ['2.', 'Nf3', 'Nc6'],
      ['3.', 'Bc4', ''],
    ])
  })

  it("marks the latest move, in White's column", () => {
    render(<MoveList moves={OPENING} />)

    const marked = markedCells()
    expect(marked.map(cell => cell.textContent)).toEqual(['Bc4'])
    expect(marked[0]).toHaveClass('bg-accent')
  })

  it("marks the latest move, in Black's column", () => {
    render(<MoveList moves={OPENING.slice(0, 4)} />)

    const marked = markedCells()
    expect(marked.map(cell => cell.textContent)).toEqual(['Nc6'])
  })

  it('marks a repeated move only where it was played last', () => {
    const repeated = [
      move(1, 'g1f3', 'Nf3'),
      move(2, 'g8f6', 'Nf6'),
      move(3, 'f3g1', 'Ng1'),
      move(4, 'f6g8', 'Ng8'),
      move(5, 'g1f3', 'Nf3'),
    ]
    render(<MoveList moves={repeated} />)

    const marked = markedCells()
    expect(marked).toHaveLength(1)
    expect(rowsOf(screen.getByRole('table'))[2]).toEqual(['3.', 'Nf3', ''])
    expect(within(screen.getAllByRole('row')[3]).getAllByRole('cell')[1]).toBe(
      marked[0]
    )
  })

  it('scrolls back to the latest move when its box changes size', () => {
    // A ResizeObserver whose callbacks the test calls, as the browser does
    // when the alert above the list takes room from it.
    const observed: (() => void)[] = []
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(private readonly callback: () => void) {}
        observe() {
          observed.push(this.callback)
        }
        disconnect() {}
      }
    )
    try {
      const { container } = render(<MoveList moves={OPENING} />)
      const scroller = container.firstElementChild as HTMLElement
      Object.defineProperty(scroller, 'scrollHeight', { value: 400 })
      scroller.scrollTop = 0

      for (const resized of observed) resized()

      expect(scroller.scrollTop).toBe(400)
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('scrolls to the latest move as moves are added', () => {
    const { container, rerender } = render(
      <MoveList moves={OPENING.slice(0, 2)} />
    )
    const scroller = container.firstElementChild as HTMLElement
    // jsdom has no layout, so the list's height is given.
    Object.defineProperty(scroller, 'scrollHeight', { value: 400 })

    rerender(<MoveList moves={OPENING} />)

    expect(scroller.scrollTop).toBe(400)
  })
})
