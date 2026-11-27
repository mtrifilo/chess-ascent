'use client'

import { CircleAlertIcon } from 'lucide-react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { authPagePath } from '@/lib/return-path'

import {
  AuthCard,
  FormField,
  SwitchLink,
  submitButtonClass,
  useAuthForm,
} from './auth-form'
import { useSignIn } from './session'

export function SignInForm() {
  const { leaving, outcome, errors, passOn, formProps } =
    useAuthForm(useSignIn())
  if (leaving) return null

  return (
    <AuthCard title="Sign in">
      {/* One message for a wrong email and a wrong password alike: the
          API does not say which was wrong, and neither does the page. */}
      {outcome?.kind === 'refused' && (
        <Alert variant="destructive" className="gap-y-2">
          <CircleAlertIcon aria-hidden />
          <AlertTitle className="tracking-normal">
            Email or password is incorrect.
          </AlertTitle>
          <AlertDescription>Check both and try again.</AlertDescription>
        </Alert>
      )}
      <form {...formProps} className="flex flex-col gap-4">
        <FormField
          label="Email"
          name="email"
          type="email"
          autoComplete="username"
          placeholder="you@example.com"
          required
          errors={errors.Email}
        />
        <FormField
          label="Password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          errors={errors.Password}
        />
        <Button type="submit" className={submitButtonClass}>
          Sign in
        </Button>
      </form>
      <SwitchLink
        question="No account?"
        label="Sign up"
        href={authPagePath('/sign-up', passOn)}
      />
    </AuthCard>
  )
}
