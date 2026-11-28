'use client'

import { useRouter } from 'next/navigation'
import { useId, useState, useTransition } from 'react'

import type { Side } from '@/components/chess-board/chess-board'
import { Button, buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'

import { useCreateGame } from './game-api'
import {
  DEFAULT_OPPONENT_STRENGTH,
  OPPONENT_STRENGTHS,
  sideLabel,
  type OpponentStrength,
} from './game-state'

const SIDES: readonly Side[] = ['white', 'black']

// The choices for a game against the computer, and "Start game", which
// creates the game and opens it: a card from `md` up, and the bare form on
// phones, as drawn.
//
// "Start game" stays disabled from the press until the new game's page has
// replaced this one, so a second press cannot start a second game: the
// navigation runs as a transition, which is pending until it commits. Should
// the API not answer with a game, the form stays as it was, choices
// included, and the query cache reports why in the browser console.
export function NewGameForm() {
  const router = useRouter()
  const createGame = useCreateGame()
  const [opening, startOpening] = useTransition()
  const [playerSide, setPlayerSide] = useState<Side>('white')
  const [strength, setStrength] = useState<OpponentStrength>(
    DEFAULT_OPPONENT_STRENGTH
  )
  const strengthHintId = useId()

  function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    createGame.mutate(
      { playerSide, strength },
      {
        onSuccess: game => startOpening(() => router.push(`/games/${game.id}`)),
      }
    )
  }

  return (
    <form
      onSubmit={onSubmit}
      className="mx-auto flex flex-col gap-6 md:w-120 md:rounded-md md:border md:bg-card md:p-6"
    >
      <h1 className="font-display text-display-s font-semibold">New game</h1>
      <Choices
        legend="Play as"
        options={SIDES}
        label={sideLabel}
        value={playerSide}
        onChange={setPlayerSide}
        className="grid-cols-2"
      />
      <div className="flex flex-col gap-2">
        <Choices
          legend="Opponent strength"
          options={OPPONENT_STRENGTHS}
          label={String}
          value={strength}
          onChange={setStrength}
          describedBy={strengthHintId}
          className="grid-cols-3 md:grid-cols-5"
        />
        <p id={strengthHintId} className="text-body-s text-muted-foreground">
          Higher numbers are stronger opponents.
        </p>
      </div>
      <Button
        type="submit"
        disabled={createGame.isPending || opening}
        className="h-12.75 w-full md:h-8 md:w-auto md:self-start md:px-2"
      >
        Start game
      </Button>
    </form>
  )
}

// One choice among a few options, drawn as a row of buttons with the chosen
// one filled. Each option is a native radio button, hidden, inside the label
// that draws it, so the group is one stop for the Tab key, the arrow keys
// move the choice, and a screen reader announces a radio group under its
// legend.
function Choices<Option extends string | number>({
  legend,
  options,
  label,
  value,
  onChange,
  describedBy,
  className,
}: {
  legend: string
  options: readonly Option[]
  label: (option: Option) => string
  value: Option
  onChange: (option: Option) => void
  describedBy?: string
  className: string
}) {
  const name = useId()
  return (
    <fieldset aria-describedby={describedBy}>
      <legend className="mb-2 text-sm font-medium">{legend}</legend>
      <div className={cn('grid gap-2', className)}>
        {options.map(option => (
          <label
            key={option}
            className={cn(
              buttonVariants({
                variant: option === value ? 'default' : 'outline',
              }),
              // The outline is drawn without a fill, so the card shows
              // through, as drawn.
              option !== value && 'bg-transparent',
              'h-12.75 cursor-pointer has-focus-visible:border-ring has-focus-visible:ring-[3px] has-focus-visible:ring-ring/50 md:h-8'
            )}
          >
            <input
              type="radio"
              name={name}
              value={option}
              checked={option === value}
              onChange={() => onChange(option)}
              className="sr-only"
            />
            {label(option)}
          </label>
        ))}
      </div>
    </fieldset>
  )
}
