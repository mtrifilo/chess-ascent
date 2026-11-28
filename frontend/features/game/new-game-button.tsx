'use client'

import { useRouter } from 'next/navigation'

import { Button } from '@/components/ui/button'

import { useCreateGame } from './game-api'

// Starts a game in which the player moves both sides, and opens it. Should
// the API not answer with a game, the button stays as it was and the query
// cache reports why in the browser console.
export function NewGameButton({ className }: { className?: string }) {
  const router = useRouter()
  const createGame = useCreateGame()
  return (
    <Button
      className={className}
      disabled={createGame.isPending}
      onClick={() =>
        createGame.mutate(undefined, {
          onSuccess: game => router.push(`/games/${game.id}`),
        })
      }
    >
      New game
    </Button>
  )
}
