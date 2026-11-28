import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { PromotionPiece } from './game-state'
import { PromotionPicker } from './promotion-picker'

const ALL: PromotionPiece[] = ['queen', 'rook', 'bishop', 'knight']

function renderPicker(
  props: Partial<React.ComponentProps<typeof PromotionPicker>> = {}
) {
  const onChoose = vi.fn()
  render(
    <PromotionPicker
      square="e8"
      side="white"
      orientation="white"
      pieces={ALL}
      onChoose={onChoose}
      {...props}
    />
  )
  const group = screen.getByRole('group')
  return { group, onChoose }
}

function optionNames(group: HTMLElement) {
  return within(group)
    .getAllByRole('button')
    .map(button => button.getAttribute('aria-label'))
}

describe('PromotionPicker', () => {
  it('is named with the approved wording for its square', () => {
    const { group } = renderPicker({ square: 'a8' })

    expect(group).toHaveAccessibleName('Choose a piece for the pawn on a8.')
  })

  it('offers queen, rook, bishop and knight, in that order', () => {
    const { group } = renderPicker()

    expect(optionNames(group)).toEqual(['Queen', 'Rook', 'Bishop', 'Knight'])
  })

  it("draws each option with the board's piece artwork in the pawn's colour", () => {
    const { group } = renderPicker({ side: 'black' })

    const pieces = [...group.querySelectorAll('.cg-wrap > piece')]
    expect(pieces.map(piece => piece.className)).toEqual([
      'queen black',
      'rook black',
      'bishop black',
      'knight black',
    ])
  })

  it('reports the piece chosen', () => {
    const { onChoose } = renderPicker()

    fireEvent.click(screen.getByRole('button', { name: 'Knight' }))

    expect(onChoose).toHaveBeenCalledExactlyOnceWith('knight')
  })

  it('opens over the promotion square and runs down from the top edge', () => {
    const { group } = renderPicker({ square: 'e8', orientation: 'white' })

    expect(group.style.left).toBe('50%')
    expect(group).toHaveClass('top-0', 'flex-col')
  })

  it('runs up from the bottom edge, so it stays on the board', () => {
    // Black promotes on b1 with White at the bottom.
    const { group } = renderPicker({
      square: 'b1',
      side: 'black',
      orientation: 'white',
    })

    expect(group.style.left).toBe('12.5%')
    expect(group).toHaveClass('bottom-0', 'flex-col-reverse')
    // Queen is still first, nearest the promotion square.
    expect(optionNames(group)[0]).toBe('Queen')
  })

  it('finds the square on a flipped board', () => {
    // White promotes on b8 with Black at the bottom: b8 is second from the
    // right, on the bottom edge.
    const { group } = renderPicker({ square: 'b8', orientation: 'black' })

    expect(group.style.left).toBe('75%')
    expect(group).toHaveClass('bottom-0')
  })

  it('offers only the pieces given', () => {
    const { group } = renderPicker({ pieces: ['queen', 'knight'] })

    expect(optionNames(group)).toEqual(['Queen', 'Knight'])
  })
})
