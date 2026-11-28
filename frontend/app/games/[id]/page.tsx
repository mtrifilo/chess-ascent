import { Suspense } from 'react'

import { GameRoute } from '@/features/game/game-route'

// Rendered only in the browser, for a signed-in visitor (see the layout of
// /games and RequireAccount).
export const instant = false

// The game's id is known only at request time, so reading it suspends while
// the route is prerendered; the fallback is an empty page.
export default function GamePage() {
  return (
    <Suspense>
      <GameRoute />
    </Suspense>
  )
}
