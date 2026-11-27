'use client'

import { Button } from '@/components/ui/button'
import { authPagePath } from '@/lib/return-path'

import {
  AuthCard,
  FormField,
  SwitchLink,
  submitButtonClass,
  useAuthFormRef,
  useFocusFirstInvalid,
  useLeaveWhenSignedIn,
  useReturnPath,
} from './auth-form'
import { useRegister } from './session'

export function SignUpForm() {
  const { returnTo, passOn } = useReturnPath()
  const leaving = useLeaveWhenSignedIn(returnTo)
  const register = useRegister()
  const { formRef, ref } = useAuthFormRef(register.reset)
  const outcome = register.data
  useFocusFirstInvalid(formRef, outcome)

  if (leaving) return null

  const errors = outcome?.kind === 'invalid' ? outcome.errors : {}
  const passwordErrors = errors.Password ?? []

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (register.isPending) return
    const fields = new FormData(event.currentTarget)
    register.mutate({
      email: String(fields.get('email')),
      password: String(fields.get('password')),
    })
  }

  return (
    <AuthCard title="Sign up">
      {/* method="post": see SignInForm. */}
      <form
        ref={ref}
        method="post"
        onSubmit={handleSubmit}
        className="flex flex-col gap-4"
      >
        <FormField
          label="Email"
          name="email"
          type="email"
          autoComplete="username"
          placeholder="you@example.com"
          required
          lead={
            outcome?.kind === 'duplicate-email'
              ? 'An account with this email already exists.'
              : undefined
          }
          errors={errors.Email}
        />
        {/* The password rules are the API's: the approved line says the
            password was refused, and the lines under it are the API's own
            description of each rule it broke. */}
        <FormField
          label="Password"
          name="password"
          type="password"
          autoComplete="new-password"
          required
          lead={
            passwordErrors.length > 0
              ? 'This password does not meet the requirements.'
              : undefined
          }
          errors={passwordErrors}
        />
        <Button type="submit" className={submitButtonClass}>
          Sign up
        </Button>
      </form>
      <SwitchLink
        question="Have an account?"
        label="Sign in"
        href={authPagePath('/sign-in', passOn)}
      />
    </AuthCard>
  )
}
