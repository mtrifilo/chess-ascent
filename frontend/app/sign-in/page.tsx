import { Suspense } from 'react'

import { SignInForm } from '@/features/auth/sign-in-form'

// The form reads its return path from the address's query, which a
// prerendered page learns only in the browser, so it renders inside its own
// boundary and the rest of the page stays in the static shell.
export default function SignInPage() {
  return (
    <Suspense>
      <SignInForm />
    </Suspense>
  )
}
