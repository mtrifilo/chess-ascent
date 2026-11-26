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
// the trigger shows the email. The email is the trigger's accessible name at
// every width.
//
// The menu opens 4px below the top bar, as drawn, rather than 4px below the
// trigger: the top margin adds the space between the trigger and the bar's
// bottom edge, 6px on phones and 12px from `md` up.
export function AccountMenu({ email }: { email: string }) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="flex size-11 shrink-0 items-center justify-center gap-1.5 rounded-md border text-sm md:size-auto md:h-8 md:border-0 md:px-3">
        <UserIcon aria-hidden className="size-4 shrink-0 md:hidden" />
        <span className="sr-only md:not-sr-only">{email}</span>
        <ChevronDownIcon
          aria-hidden
          className="hidden size-4 shrink-0 md:block"
        />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="mt-1.5 w-56 md:mt-3">
        <DropdownMenuLabel className="truncate text-body-s font-normal text-muted-foreground">
          {email}
        </DropdownMenuLabel>
        <DropdownMenuSeparator className="mx-0 my-0" />
        <DropdownMenuItem className="h-11 md:h-8">Sign out</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
