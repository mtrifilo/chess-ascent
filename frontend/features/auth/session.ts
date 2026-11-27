'use client'

import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from '@tanstack/react-query'

import { api } from '@/lib/api'
import { reloadPage } from '@/lib/page-load'
import type { components } from '@/types/api'

// The signed-in account, or null when nobody is signed in.
type Session = components['schemas']['CurrentUserResponse'] | null

type Credentials = { email: string; password: string }

// The error lists of a validation problem, keyed by the request's C# property
// names (Email, Password), as the API reports them.
export type FieldErrors = { Email?: string[]; Password?: string[] }

type AuthOutcome<Refused extends string> =
  | { kind: 'signed-in' }
  | { kind: Refused }
  | { kind: 'invalid'; errors: FieldErrors }

const SESSION_KEY = ['session'] as const

// Who is signed in, read from the API in the browser. Every component that
// asks shares one cached answer, and signing in, signing up or signing out
// replaces it, so they all change together. `data` is undefined until an
// answer arrives, and stays undefined if the API cannot be asked (`isError`):
// in both cases the page knows neither that someone is signed in nor that
// nobody is.
export function useSession() {
  return useQuery({ queryKey: SESSION_KEY, queryFn: fetchSession })
}

// The query is asked again on its own, for instance when the window regains
// focus. If it then finds that the account this page knew about is no
// longer the one signed in (signed out or replaced in another tab, or
// expired), the page is loaded again from scratch, as for any other 401 (see
// page-load.ts), rather than carrying on with what it held for that account.
//
// Passing `signal` lets TanStack Query abort the request when the query is
// cancelled, as sign-in does before it records the new session; an answer to
// a cancelled request is never acted on.
async function fetchSession({
  client,
  signal,
}: {
  client: QueryClient
  signal: AbortSignal
}): Promise<Session> {
  const before = client.getQueryData<Session>(SESSION_KEY)
  const { data, response } = await api.GET('/auth/me', { signal })
  const session = response.status === 401 ? null : data
  if (session === undefined) {
    throw new Error(`GET /auth/me answered ${response.status}.`)
  }
  if (!signal.aborted && before && session?.email !== before.email) {
    reloadPage()
    // The page is about to be replaced. Answering with the session it
    // already shows keeps anything, such as RequireAccount's own redirect,
    // from acting on the change before the reload does.
    return before
  }
  return session
}

// A refused sign-in (401) is one answer for a wrong email and a wrong
// password alike; a refused registration (409) is an email that already has
// an account.
export function useSignIn() {
  return useCredentialsMutation('/auth/sign-in', {
    status: 401,
    kind: 'refused',
  })
}

export function useRegister() {
  return useCredentialsMutation('/auth/register', {
    status: 409,
    kind: 'duplicate-email',
  })
}

// Signs in or registers, which answer alike: 200 with the account, which
// becomes the session; 400 with field errors; or `refusal`. The approved
// screens tell apart only these. Anything else, such as the API being
// unreachable, is thrown and leaves the form as it was.
//
// The email is trimmed here, so white space typed or pasted around it never
// reaches the API. The password is sent as typed, and is not kept: the
// mutation cache holds each attempt's variables, password included, and
// `gcTime: 0` drops an attempt as soon as no form is showing its answer.
function useCredentialsMutation<Refused extends string>(
  path: '/auth/sign-in' | '/auth/register',
  refusal: { status: number; kind: Refused }
) {
  const queryClient = useQueryClient()
  return useMutation({
    gcTime: 0,
    mutationFn: async ({
      email,
      password,
    }: Credentials): Promise<AuthOutcome<Refused>> => {
      const { data, error, response } = await api.POST(path, {
        body: { email: email.trim(), password },
      })
      if (data) {
        // A session request sent before this one, which will answer that
        // nobody is signed in, must not land after the new session and
        // replace it.
        await queryClient.cancelQueries({ queryKey: SESSION_KEY })
        queryClient.setQueryData<Session>(SESSION_KEY, data)
        return { kind: 'signed-in' }
      }
      if (response.status === refusal.status) return { kind: refusal.kind }
      if (response.status === 400) return invalid(error)
      throw unexpected(path, response)
    },
  })
}

// Once the API has ended the session, the page is loaded again from scratch
// (see page-load.ts). It then finds nobody signed in: the bar shows only the
// product name, and a page that needs an account sends the visitor to sign
// in.
export function useSignOut() {
  return useMutation({
    mutationFn: async () => {
      const { response } = await api.POST('/auth/sign-out')
      if (response.status !== 204) throw unexpected('sign-out', response)
      reloadPage()
    },
  })
}

// A 400's body is the API's validation problem, whose `errors` may also hold
// keys for other properties; only the two fields the forms have are kept.
function invalid(
  problem: components['schemas']['HttpValidationProblemDetails'] | undefined
): { kind: 'invalid'; errors: FieldErrors } {
  const errors: FieldErrors = problem?.errors ?? {}
  return {
    kind: 'invalid',
    errors: { Email: errors.Email, Password: errors.Password },
  }
}

// Names the call and the status only: the request body, which holds the
// password, never goes into an error.
function unexpected(call: string, response: Response) {
  return new Error(`${call} answered ${response.status}.`)
}
