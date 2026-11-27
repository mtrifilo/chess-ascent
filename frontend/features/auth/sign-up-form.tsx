'use client'

import { Button } from '@/components/ui/button'
import { authPagePath } from '@/lib/return-path'

import {
  AuthCard,
  FormField,
  SwitchLink,
  submitButtonClass,
  useAuthForm,
} from './auth-form'
import { useRegister } from './session'

export function SignUpForm() {
  const { leaving, outcome, errors, passOn, formProps } =
    useAuthForm(useRegister())
  if (leaving) return null

  const passwordErrors = errors.Password ?? []

  return (
    <AuthCard title="Sign up">
      <form {...formProps} className="flex flex-col gap-4">
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
