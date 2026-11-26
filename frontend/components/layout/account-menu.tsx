import { ChevronDownIcon, UserIcon } from 'lucide-react'

import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'

// Phones get a 44px icon-only trigger and 44px items, for touch; from `md` up
// the trigger shows the email, cut short with an ellipsis when the bar runs
// out of room. The trigger's accessible name is the email at every width.
// The menu shows the whole email, wrapping a long one.
//
// The menu opens 4px below the top bar, as drawn, rather than 4px below the
// trigger. Its top margin is the gap between the trigger's bottom edge and
// the bar's: the bar is 56px (`h-14` in app-shell.tsx, with `pt-px` balancing
// its 1px bottom border) and the trigger is centred in it, so the gap is
// (56 - 44) / 2 = 6px on phones and (56 - 32) / 2 = 12px from `md` up.
// Changing either height means changing `mt-1.5 md:mt-3` here.
const MENU_GAP_BELOW_BAR = 4

export function AccountMenu({ email }: { email: string }) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={email}
        className="flex size-11 shrink-0 items-center justify-center gap-1.5 rounded-md border text-sm outline-hidden focus-visible:ring-[3px] focus-visible:ring-ring md:size-auto md:h-8 md:min-w-0 md:shrink md:border-0 md:px-3"
      >
        <UserIcon aria-hidden className="size-4 shrink-0 md:hidden" />
        <span className="hidden md:block md:truncate">{email}</span>
        <ChevronDownIcon
          aria-hidden
          className="hidden size-4 shrink-0 md:block"
        />
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        sideOffset={MENU_GAP_BELOW_BAR}
        className="mt-1.5 w-56 md:mt-3"
      >
        <DropdownMenuLabel className="text-body-s font-normal wrap-anywhere text-muted-foreground">
          {email}
        </DropdownMenuLabel>
        <DropdownMenuSeparator className="mx-0 my-0" />
        <DropdownMenuItem className="h-11 md:h-8">Sign out</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
