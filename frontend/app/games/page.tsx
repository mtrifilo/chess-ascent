import { MyGamesScreen } from '@/features/my-games/my-games-screen'

// Rendered only in the browser, for a signed-in visitor (see layout.tsx and
// RequireAccount).
export const instant = false

export default function GamesPage() {
  return <MyGamesScreen />
}
