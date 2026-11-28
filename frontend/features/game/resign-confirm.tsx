'use client'

import { useEffect, useId, useRef } from 'react'

import { Button } from '@/components/ui/button'

import { useResign } from './game-api'

// The step between pressing Resign and resigning, drawn inline in place of
// the game controls rather than as a modal: the question, then Cancel, then
// Resign in the destructive style. Resigning answers with the finished game,
// which the screen then shows in place of all this.
//
// Focus moves to Cancel when it opens, so a stray Enter does nothing that
// cannot be undone, and the group is named by the question, which a screen
// reader reads as focus enters it.
//
// A press that cannot act is refused without changing how the button looks,
// as the designs draw no such state: Resign while a move is being sent or
// the computer is thinking (`moveUnderway`), and both buttons while the
// resignation is on its way, so that it is sent once and is not cancelled
// after it has gone. A resignation that fails leaves this open, with no
// message, so that Resign can be pressed again.
export function ResignConfirm({
  gameId,
  moveUnderway,
  onCancel,
}: {
  gameId: string
  moveUnderway: boolean
  onCancel: () => void
}) {
  const questionId = useId()
  const cancel = useRef<HTMLButtonElement>(null)
  useEffect(() => cancel.current?.focus(), [])

  const resign = useResign(gameId)
  // Set at the press itself: the mutation reports itself pending only after
  // React has rendered again, which a second press can come before.
  const sending = useRef(false)

  function confirm() {
    if (moveUnderway || sending.current) return
    sending.current = true
    resign.mutate(undefined, {
      onSettled: () => {
        sending.current = false
      },
    })
  }

  return (
    <div
      role="group"
      aria-labelledby={questionId}
      className="flex flex-col gap-3 rounded-md border bg-background p-3 md:order-last"
    >
      <p id={questionId} className="text-sm">
        Resign this game? This cannot be undone.
      </p>
      <div className="flex gap-2">
        <Button
          ref={cancel}
          variant="outline"
          aria-disabled={resign.isPending}
          className="h-12.75 flex-1 px-4 md:h-8 md:flex-none md:px-2"
          onClick={() => {
            if (!sending.current) onCancel()
          }}
        >
          Cancel
        </Button>
        <Button
          variant="destructive"
          aria-disabled={moveUnderway || resign.isPending}
          className="h-12.75 flex-1 px-4 md:h-8 md:flex-none md:px-2"
          onClick={confirm}
        >
          Resign
        </Button>
      </div>
    </div>
  )
}
