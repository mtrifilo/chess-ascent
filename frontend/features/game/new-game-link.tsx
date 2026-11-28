import Link from 'next/link'

import { Button } from '@/components/ui/button'

// "New game", which opens the new-game screen: the full width of its panel
// on phones, and its own width from `md` up, as drawn in the result panel
// and in My games' empty state.
export function NewGameLink() {
  return (
    <Button
      asChild
      className="h-12.75 w-full md:h-8 md:w-auto md:self-start md:px-2"
    >
      <Link href="/play">New game</Link>
    </Button>
  )
}
