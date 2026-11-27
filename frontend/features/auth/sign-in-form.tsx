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
  useAuthFormRef,
  useFocusFirstInvalid,
  useLeaveWhenSignedIn,
  useReturnPath,
} from './auth-form'
import { useSignIn } from './session'

export function SignInForm() {
  const { returnTo, passOn } = useReturnPath()
  const leaving = useLeaveWhenSignedIn(returnTo)
  const signIn = useSignIn()
  const { formRef, ref } = useAuthFormRef(signIn.reset)
  const outcome = signIn.data
  useFocusFirstInvalid(formRef, outcome)

  if (leaving) return null

  const errors = outcome?.kind === 'invalid' ? outcome.errors : {}

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (signIn.isPending) return
    const fields = new FormData(event.currentTarget)
    signIn.mutate({
      email: String(fields.get('email')),
      password: String(fields.get('password')),
    })
  }

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
      {/* method="post": if the form is submitted before the page's script
          has loaded, the browser posts the fields to this page instead of
          putting them, the password included, in the address. */}
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
