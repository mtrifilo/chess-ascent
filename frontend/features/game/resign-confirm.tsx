'use client'

import { useEffect, useId, useRef } from 'react'

import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

import { TEXT_CONTROL_CLASS } from './controls'

// The step between pressing Resign and resigning, drawn inline in place of
// the game controls rather than as a modal: the question, then Cancel, then
// Resign in the destructive style. The screen sends the resignation and
// places this where the controls were.
//
// Focus moves to Cancel when it opens, so a stray Enter does nothing that
// cannot be undone, and the group is named by the question, which a screen
// reader reads as focus enters it.
//
// A press that cannot act is refused without changing how the button looks,
// as the designs draw no such state: Resign while a move is being sent or
// the computer is thinking (`moveUnderway`), and both buttons while the
// resignation is on its way (`sending`).
export function ResignConfirm({
  moveUnderway,
  sending,
  onCancel,
  onResign,
  className,
}: {
  moveUnderway: boolean
  sending: boolean
  onCancel: () => void
  onResign: () => void
  className?: string
}) {
  const questionId = useId()
  const cancel = useRef<HTMLButtonElement>(null)
  useEffect(() => cancel.current?.focus(), [])

  return (
    <div
      role="group"
      aria-labelledby={questionId}
      className={cn(
        'flex flex-col gap-3 rounded-md border bg-background p-3',
        className
      )}
    >
      <p id={questionId} className="text-sm">
        Resign this game? This cannot be undone.
      </p>
      {/* Two equal halves on phones, as drawn, whatever each button's border. */}
      <div className="grid grid-cols-2 gap-2 md:flex">
        <Button
          ref={cancel}
          variant="outline"
          aria-disabled={sending}
          className={TEXT_CONTROL_CLASS}
          onClick={onCancel}
        >
          Cancel
        </Button>
        <Button
          variant="destructive"
          aria-disabled={moveUnderway || sending}
          className={TEXT_CONTROL_CLASS}
          onClick={onResign}
        >
          Resign
        </Button>
      </div>
    </div>
  )
}
