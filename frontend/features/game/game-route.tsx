'use client'

import { useParams, useRouter } from 'next/navigation'
import { useEffect, useState } from 'react'

import { ReplayScreen } from '@/features/replay/replay-screen'

import { useGame } from './game-api'
import { GameScreen } from './game-screen'

// The game named by the address, /games/<id>. Each game gets a screen of its
// own, so moving from one game to another starts afresh.
export function GameRoute() {
  const { id } = useParams<{ id: string }>()
  return <GameOrReplay key={id} id={id} />
}

// A game that is over when its page opens is replayed. One that ends while
// the game screen shows it stays there, with its result, until the page is
// opened again.
//
// The page opens when it is first shown, and again each time the player
// arrives at it by a link: Next.js keeps a page the player leaves, hidden,
// and shows it again as it was (cacheComponents in next.config.ts), and its
// bfcacheId changes on an arrival by a link but not on the back and forward
// buttons, which bring the page back as it was left. The screens stay
// mounted across an arrival, so a move or a resignation still on its way is
// not lost.
//
// On opening, the screen is chosen from the game as the server then has it:
// a game the cache already holds as finished is replayed at once, since a
// finished game stays finished; one it holds as still being played waits
// for the read this opening asks for, on the game screen meanwhile, since
// it may have ended elsewhere. The choice is made while rendering, in the
// same pass in which the game arrives, so a finished game opened afresh
// never shows the game screen first. The game screen shows nothing until
// the game has loaded, and both read it with one request.
function GameOrReplay({ id }: { id: string }) {
  const { bfcacheId } = useRouter()
  const { data: game, dataUpdatedAt, refetch } = useGame(id)
  const [opening, setOpening] = useState<Opening>({
    bfcacheId,
    readBefore: dataUpdatedAt,
    replay: undefined,
  })

  if (opening.bfcacheId !== bfcacheId) {
    setOpening({ bfcacheId, readBefore: dataUpdatedAt, replay: undefined })
  } else if (opening.replay === undefined && game) {
    const finished = game.status === 'finished'
    if (finished || dataUpdatedAt > opening.readBefore) {
      setOpening({ ...opening, replay: finished })
    }
  }

  // Each opening reads the game, joining a read already on its way.
  useEffect(() => {
    void refetch({ cancelRefetch: false })
  }, [bfcacheId, refetch])

  return opening.replay && game ? (
    <ReplayScreen game={game} />
  ) : (
    <GameScreen id={id} />
  )
}

// One opening of the page: the navigation that opened it, when the game was
// last read before it, and the screen chosen, once it is.
type Opening = {
  bfcacheId: string
  readBefore: number
  replay: boolean | undefined
}
