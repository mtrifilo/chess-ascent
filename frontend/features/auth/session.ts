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
export type Session = components['schemas']['CurrentUserResponse'] | null

export type Credentials = { email: string; password: string }

// The error lists of a validation problem, keyed by the request's C# property
// names (Email, Password), as the API reports them.
export type FieldErrors = { Email?: string[]; Password?: string[] }

export type SignInOutcome =
  | { kind: 'signed-in' }
  | { kind: 'refused' }
  | { kind: 'invalid'; errors: FieldErrors }

export type RegisterOutcome =
  | { kind: 'signed-in' }
  | { kind: 'duplicate-email' }
  | { kind: 'invalid'; errors: FieldErrors }

const SESSION_KEY = ['session'] as const

// Who is signed in, read from the API in the browser. Every component that
// asks shares one cached answer, and signing in, signing up or signing out
// replaces it, so they all change together. While the first answer is on its
// way, `data` is undefined: neither signed in nor signed out.
export function useSession() {
  return useQuery({ queryKey: SESSION_KEY, queryFn: fetchSession })
}

async function fetchSession(): Promise<Session> {
  const { data, response } = await api.GET('/auth/me')
  if (response.status === 401) return null
  if (data) return data
  throw new Error(`GET /auth/me answered ${response.status}.`)
}

function setSession(queryClient: QueryClient, session: Session) {
  queryClient.setQueryData(SESSION_KEY, session)
}

// The approved screens tell apart only the answers below. Anything else, such
// as the API being unreachable, is thrown and leaves the form as it was.
//
// The email is trimmed here, so white space typed or pasted around it never
// reaches the API. The password is sent as typed.
export function useSignIn() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({
      email,
      password,
    }: Credentials): Promise<SignInOutcome> => {
      const { data, error, response } = await api.POST('/auth/sign-in', {
        body: { email: email.trim(), password },
      })
      if (data) {
        setSession(queryClient, data)
        return { kind: 'signed-in' }
      }
      if (response.status === 401) return { kind: 'refused' }
      if (response.status === 400) return invalid(error)
      throw unexpected('sign-in', response)
    },
  })
}

export function useRegister() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({
      email,
      password,
    }: Credentials): Promise<RegisterOutcome> => {
      const { data, error, response } = await api.POST('/auth/register', {
        body: { email: email.trim(), password },
      })
      if (data) {
        setSession(queryClient, data)
        return { kind: 'signed-in' }
      }
      if (response.status === 409) return { kind: 'duplicate-email' }
      if (response.status === 400) return invalid(error)
      throw unexpected('register', response)
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
