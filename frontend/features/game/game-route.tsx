'use client'

import { notFound, useParams } from 'next/navigation'
import { useState } from 'react'

import { ReplayScreen } from '@/features/replay/replay-screen'

import { useGame } from './game-api'
import { GameScreen } from './game-screen'
import type { Game } from './game-state'

// The game named by the address, /games/<id>. Each game gets a screen of its
// own, so moving from one game to another starts afresh.
export function GameRoute() {
  const { id } = useParams<{ id: string }>()
  return <GameOrReplay key={id} id={id} />
}

// Nothing shows until the game has loaded, not even a highlighted item in the
// top bar, since which screen it gets depends on the game. A game the player
// does not have shows the site's not-found page, as the game screen does.
function GameOrReplay({ id }: { id: string }) {
  const { data: game } = useGame(id)
  if (game === null) notFound()
  if (!game) return null
  return <OpenedGame game={game} />
}

// A game that is over when its page opens is replayed. One that ends while
// the game screen shows it stays there, with its result, until the page is
// opened again: the screen is chosen once, from the game as first loaded.
function OpenedGame({ game }: { game: Game }) {
  const [replay] = useState(game.status === 'finished')
  return replay ? <ReplayScreen game={game} /> : <GameScreen id={game.id} />
}
