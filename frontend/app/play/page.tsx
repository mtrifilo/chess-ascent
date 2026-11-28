import { NewGameForm } from '@/features/game/new-game-form'

// Rendered only in the browser, for a signed-in visitor (see layout.tsx and
// RequireAccount).
export const instant = false

export default function PlayPage() {
  return <NewGameForm />
}
