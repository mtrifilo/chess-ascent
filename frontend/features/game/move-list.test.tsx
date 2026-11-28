import { fireEvent, render, screen, within } from '@testing-library/react'
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
    // A ResizeObserver the test drives, as the browser does when the alert
    // above the list takes room from it. Each records what it watches and
    // whether it has been disconnected.
    const observers: {
      target?: Element
      callback: () => void
      connected: boolean
    }[] = []
    vi.stubGlobal(
      'ResizeObserver',
      class {
        private readonly record
        constructor(callback: () => void) {
          this.record = { callback, connected: true } as (typeof observers)[0]
          observers.push(this.record)
        }
        observe(target: Element) {
          this.record.target = target
        }
        disconnect() {
          this.record.connected = false
        }
      }
    )
    try {
      const { container, rerender, unmount } = render(
        <MoveList moves={OPENING.slice(0, 4)} />
      )
      const scroller = container.firstElementChild as HTMLElement
      Object.defineProperty(scroller, 'scrollHeight', { value: 400 })
      scroller.scrollTop = 0

      const live = () => observers.filter(observer => observer.connected)
      expect(live().map(observer => observer.target)).toEqual([scroller])
      live()[0].callback()
      expect(scroller.scrollTop).toBe(400)

      // One observer at a time, as moves are added and when the list goes.
      rerender(<MoveList moves={OPENING} />)
      expect(live().map(observer => observer.target)).toEqual([scroller])
      unmount()
      expect(live()).toEqual([])
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('marks the move with the half-move number it is given rather than the latest', () => {
    render(<MoveList moves={OPENING} current={2} />)

    expect(markedCells().map(cell => cell.textContent)).toEqual(['e5'])
  })

  it('makes each move a button that reports its half-move number, marking the current one', () => {
    const onSelect = vi.fn()
    render(<MoveList moves={OPENING} current={3} onSelect={onSelect} />)

    const buttons = screen.getAllByRole('button')
    expect(buttons.map(button => button.textContent)).toEqual([
      'e4',
      'e5',
      'Nf3',
      'Nc6',
      'Bc4',
    ])
    expect(
      buttons.filter(button => button.getAttribute('aria-current') === 'step')
    ).toEqual([buttons[2]])
    fireEvent.click(buttons[3])
    expect(onSelect).toHaveBeenCalledWith(4)
  })

  it('has no buttons when nothing is told of a click', () => {
    render(<MoveList moves={OPENING} />)

    expect(screen.queryAllByRole('button')).toEqual([])
  })

  it('scrolls a marked move above its box down into view, below the heading', () => {
    const { container } = render(
      <MoveList moves={OPENING} current={5} onSelect={() => {}} />
    )
    const scroller = container.firstElementChild as HTMLElement
    // jsdom has no layout, so the boxes are given: the list's box from 0 to
    // 100 with a 20px heading, scrolled so that the marked move sits above it.
    const box = (top: number, height: number) =>
      ({ top, bottom: top + height, height }) as DOMRect
    scroller.getBoundingClientRect = () => box(0, 100)
    container.querySelector('thead')!.getBoundingClientRect = () => box(0, 20)
    const cellAt = (index: number) =>
      screen.getAllByRole('button')[index].closest('td')!
    cellAt(0).getBoundingClientRect = () => box(-30, 28)
    scroller.scrollTop = 200

    render(<MoveList moves={OPENING} current={1} onSelect={() => {}} />, {
      container,
    })

    expect(scroller.scrollTop).toBe(150)
  })

  it('scrolls a marked move below its box into view', () => {
    const { container } = render(
      <MoveList moves={OPENING} current={1} onSelect={() => {}} />
    )
    const scroller = container.firstElementChild as HTMLElement
    // The list's box from 0 to 100, with the marked move 30px below it.
    const box = (top: number, height: number) =>
      ({ top, bottom: top + height, height }) as DOMRect
    scroller.getBoundingClientRect = () => box(0, 100)
    container.querySelector('thead')!.getBoundingClientRect = () => box(0, 20)
    screen.getAllByRole('button')[3].closest('td')!.getBoundingClientRect =
      () => box(102, 28)
    scroller.scrollTop = 0

    render(<MoveList moves={OPENING} current={4} onSelect={() => {}} />, {
      container,
    })

    expect(scroller.scrollTop).toBe(30)
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
