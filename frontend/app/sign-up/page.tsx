import { Suspense } from 'react'

import { SignUpForm } from '@/features/auth/sign-up-form'

// See app/sign-in/page.tsx for the boundary.
export default function SignUpPage() {
  return (
    <Suspense>
      <SignUpForm />
    </Suspense>
  )
}
