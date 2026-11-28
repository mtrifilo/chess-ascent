'use client'

import { useParams } from 'next/navigation'

import { GameScreen } from './game-screen'

// The game named by the address, /games/<id>.
export function GameRoute() {
  const { id } = useParams<{ id: string }>()
  return <GameScreen id={id} />
}
