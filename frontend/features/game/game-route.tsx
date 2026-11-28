'use client'

import { useParams, useRouter } from 'next/navigation'
import { useState } from 'react'

import { ReplayScreen } from '@/features/replay/replay-screen'

import { useGame } from './game-api'
import { GameScreen } from './game-screen'

// The game named by the address, /games/<id>.
//
// Next.js keeps a page the player leaves, hidden, and shows it again as it
// was when they come back (cacheComponents in next.config.ts). The screen is
// keyed on the navigation as well as the game, so arriving by a link starts
// afresh and chooses again from the game as it is then, as opening the page
// does, while the back and forward buttons bring the page back as it was
// left. bfcacheId changes on the first and not on the second.
export function GameRoute() {
  const { id } = useParams<{ id: string }>()
  const { bfcacheId } = useRouter()
  return <GameOrReplay key={`${id} ${bfcacheId}`} id={id} />
}

// A game that is over when its page opens is replayed. One that ends while
// the game screen shows it stays there, with its result, until the page is
// opened again: the screen is chosen once, from the game as first loaded.
//
// The game screen is mounted from the start and shows nothing until the game
// has loaded, so both read the game with one request. The choice is made
// while rendering, in the same pass in which the game arrives, so a game
// that is over never shows the game screen first.
function GameOrReplay({ id }: { id: string }) {
  const { data: game } = useGame(id)
  const [replay, setReplay] = useState<boolean>()
  if (game && replay === undefined) setReplay(game.status === 'finished')
  return replay && game ? <ReplayScreen game={game} /> : <GameScreen id={id} />
}
