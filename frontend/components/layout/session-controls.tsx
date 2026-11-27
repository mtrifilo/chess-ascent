'use client'

import { Suspense } from 'react'

import { useSession } from '@/features/auth/session'

import { AccountMenu } from './account-menu'
import { ActiveNavLinks } from './active-nav-links'
import { NavLinks } from './nav-links'

// The top bar's links and account menu, shown only while someone is signed
// in.
export function SessionControls() {
  const { data: session } = useSession()
  return session ? <SignedInControls email={session.email} /> : null
}

// The spacer sits before the links on phones, pushing them right with the
// account menu, and after them from `md` up, where the links stay beside the
// product name.
function SignedInControls({ email }: { email: string }) {
  return (
    <>
      <div aria-hidden className="flex-1 md:hidden" />
      {/* The current path is unknown while a route with a dynamic
          segment, such as one game's page, is prerendered, and Cache
          Components fails the build if it is read outside Suspense.
          The fallback is the same links with none highlighted. */}
      <Suspense fallback={<NavLinks />}>
        <ActiveNavLinks />
      </Suspense>
      <div aria-hidden className="hidden flex-1 md:block" />
      <AccountMenu email={email} />
    </>
  )
}
