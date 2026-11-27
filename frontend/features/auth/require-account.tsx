'use client'

import { useRouter } from 'next/navigation'
import { useEffect } from 'react'

import { authPagePath, currentPath } from '@/lib/return-path'

import { useSession } from './session'

// Shows its children only to a signed-in visitor. A visitor who is signed
// out is sent to sign in, and back to this page afterwards. Until the API has
// said who is signed in, nothing is shown, so a signed-out visitor never sees
// the page.
//
// This decides what the browser shows, not what the visitor may do: the API
// refuses anything that needs an account on its own.
//
// A page inside it is never rendered on the server, so Next.js's development
// check for instant navigation reports it as "dropped from rendering". Each
// such page sets `export const instant = false`, which only turns that check
// off for the page.
export function RequireAccount({ children }: { children: React.ReactNode }) {
  const { data: session } = useSession()
  const router = useRouter()
  const signedOut = session === null

  useEffect(() => {
    if (signedOut) router.replace(authPagePath('/sign-in', currentPath()))
  }, [signedOut, router])

  return session ? children : null
}
