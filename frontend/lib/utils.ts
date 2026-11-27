import { clsx, type ClassValue } from 'clsx'
import { extendTailwindMerge } from 'tailwind-merge'

// tailwind-merge knows Tailwind's stock theme only. The text sizes and the
// shadow added in app/globals.css are registered so that `text-body-s` and a
// `text-muted-foreground` passed together both survive: unregistered, the
// merger reads both as text colours and keeps only the last.
const twMerge = extendTailwindMerge({
  extend: {
    theme: {
      text: ['body-s', 'display-m', 'data-m', 'product-name'],
      shadow: ['popover'],
    },
  },
})

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
