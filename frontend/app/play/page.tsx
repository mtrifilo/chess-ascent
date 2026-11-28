import { PageHeading } from '@/components/layout/page-heading'
import { NewGameButton } from '@/features/game/new-game-button'

// Rendered only in the browser, for a signed-in visitor (see layout.tsx and
// RequireAccount).
export const instant = false

// For now Play starts a game in which the player moves both sides.
export default function PlayPage() {
  return (
    <>
      <PageHeading>Play</PageHeading>
      <NewGameButton className="mt-4" />
    </>
  )
}
