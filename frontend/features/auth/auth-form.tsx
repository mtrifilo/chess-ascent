'use client'

import Link from 'next/link'
import { useRouter, useSearchParams } from 'next/navigation'
import { useCallback, useEffect, useId, useRef } from 'react'

import { PageHeading } from '@/components/layout/page-heading'
import { buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'
import { RETURN_PATH_PARAM, returnPathFrom } from '@/lib/return-path'

import { useSession, type FieldErrors } from './session'

// What a sign-in or sign-up form needs from its mutation.
type CredentialsMutation<Outcome> = {
  data: Outcome | undefined
  isPending: boolean
  mutate: (credentials: { email: string; password: string }) => void
  reset: () => void
}

// Everything around the fields of a sign-in or sign-up form: where to go
// afterwards, the last answer and its field errors, and the props for the
// <form> element.
//
// The form has method="post": if it is submitted before the page's script
// has loaded, the browser posts the fields to the page instead of putting
// them, the password included, in the address.
export function useAuthForm<Outcome extends { kind: string }>(
  mutation: CredentialsMutation<Outcome>
) {
  const { returnTo, passOn } = useReturnPath()
  const leaving = useLeaveWhenSignedIn(returnTo)
  const { formRef, ref } = useAuthFormRef(mutation.reset)
  const outcome = mutation.data
  useFocusFirstInvalid(formRef, outcome)

  const errors: FieldErrors =
    outcome && 'errors' in outcome ? (outcome.errors as FieldErrors) : {}

  function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (mutation.isPending) return
    const fields = new FormData(event.currentTarget)
    mutation.mutate({
      email: String(fields.get('email')),
      password: String(fields.get('password')),
    })
  }

  return {
    leaving,
    outcome,
    errors,
    passOn,
    formProps: { ref, method: 'post', onSubmit },
  }
}

// The return path this page was opened with, if any. `returnTo` is where to
// go once signed in; `passOn` is what the link to the other auth page
// carries, so switching between sign-in and sign-up keeps it.
function useReturnPath() {
  const requested = useSearchParams().get(RETURN_PATH_PARAM)
  const returnTo = returnPathFrom(requested)
  return { returnTo, passOn: requested === null ? undefined : returnTo }
}

// Sends a visitor who is signed in on to `returnTo`. It covers both a
// visitor who opens the page already signed in and one who has just signed
// in or up on it, because both forms record the new session before they
// return. Returns whether the visitor is signed in, so the form can stay
// hidden while the browser leaves.
function useLeaveWhenSignedIn(returnTo: string): boolean {
  const { data: session } = useSession()
  const router = useRouter()
  const signedIn = Boolean(session)

  useEffect(() => {
    if (signedIn) router.replace(returnTo)
  }, [signedIn, returnTo, router])

  return signedIn
}

// The form's ref, and the form itself for code that reads it.
//
// Next.js keeps a page the visitor leaves in the document, hidden with
// React's <Activity>, so that going back restores it, form fields included.
// A form left that way would keep the typed password in a page nobody is
// looking at, and show its last answer when the visitor came back. So when
// the form is hidden or removed, its fields are cleared and the answer
// forgotten: React calls the cleanup a callback ref returns at that moment.
function useAuthFormRef(forgetAnswer: () => void) {
  const formRef = useRef<HTMLFormElement | null>(null)
  const ref = useCallback(
    (form: HTMLFormElement | null) => {
      formRef.current = form
      return () => {
        form?.reset()
        forgetAnswer()
        formRef.current = null
      }
    },
    [forgetAnswer]
  )
  return { formRef, ref }
}

// After an answer that marks a field invalid, moves focus to the first such
// field, as the browser does for its own validation, so a keyboard or
// screen-reader user lands on the field and hears its error.
function useFocusFirstInvalid(
  form: React.RefObject<HTMLFormElement | null>,
  outcome: unknown
) {
  useEffect(() => {
    form.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
  }, [form, outcome])
}

// The page around either form: a card from `md` up, and the bare form on
// phones, as drawn. The heading is Display/M on phones and Display/S above.
//
// The top padding adds to the page area's own (app-shell.tsx: 16px on
// phones, 32px from `md` up), giving the 24px and 64px drawn.
export function AuthCard({
  title,
  children,
}: {
  title: string
  children: React.ReactNode
}) {
  return (
    <div className="pt-2 md:pt-8">
      <div className="mx-auto flex flex-col gap-4 md:w-100 md:rounded-md md:border md:bg-card md:p-6">
        <PageHeading className="md:text-display-s">{title}</PageHeading>
        {children}
      </div>
    </div>
  )
}

// A labelled input with the errors the API reported for it. `lead` is the
// first error line when the form words it itself; `errors` are shown as the
// API words them, each on its own line.
export function FormField({
  label,
  lead,
  errors = [],
  ...inputProps
}: {
  label: string
  lead?: string
  errors?: readonly string[]
} & React.ComponentProps<typeof Input>) {
  const inputId = useId()
  const errorsId = useId()
  const lines = lead === undefined ? errors : [lead, ...errors]
  const invalid = lines.length > 0

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={inputId} className="leading-5">
        {label}
      </Label>
      <Input
        id={inputId}
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? errorsId : undefined}
        className="h-11 px-2 md:h-8"
        {...inputProps}
      />
      {invalid && (
        <ul
          id={errorsId}
          className="text-body-s wrap-anywhere text-destructive"
        >
          {lines.map(line => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      )}
    </div>
  )
}

export const submitButtonClass = 'h-12.75 w-full md:h-8'

// "No account? Sign up" and "Have an account? Sign in". The link is a
// 51px-tall touch target on phones and 32px from `md` up, as drawn.
export function SwitchLink({
  question,
  label,
  href,
}: {
  question: string
  label: string
  href: string
}) {
  return (
    <p className="flex items-center gap-1 text-sm text-muted-foreground">
      {question}
      <Link
        href={href}
        className={cn(
          buttonVariants({ variant: 'link' }),
          'h-12.75 px-4 md:h-8 md:px-2'
        )}
      >
        {label}
      </Link>
    </p>
  )
}
