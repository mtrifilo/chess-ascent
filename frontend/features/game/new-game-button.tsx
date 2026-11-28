'use client'

import { useRouter } from 'next/navigation'
import { useTransition } from 'react'

import { Button } from '@/components/ui/button'

import { useCreateGame } from './game-api'

// Starts a game in which the player moves both sides, and opens it. Should
// the API not answer with a game, the button stays as it was and the query
// cache reports why in the browser console.
//
// The button stays disabled from the click until the new game's page has
// replaced this one, so a second click cannot start a second game: the
// navigation runs as a transition, which is pending until it commits.
export function NewGameButton({ className }: { className?: string }) {
  const router = useRouter()
  const createGame = useCreateGame()
  const [opening, startOpening] = useTransition()
  return (
    <Button
      className={className}
      disabled={createGame.isPending || opening}
      onClick={() =>
        createGame.mutate(undefined, {
          onSuccess: game =>
            startOpening(() => router.push(`/games/${game.id}`)),
        })
      }
    >
      New game
    </Button>
  )
}
