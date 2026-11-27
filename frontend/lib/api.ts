import createClient, { type Middleware } from 'openapi-fetch'

import type { paths } from '@/types/api'

import { loadPage } from './page-load'
import { authPagePath, currentPath } from './return-path'

// The typed client for the back end. `paths` is generated from the API's
// OpenAPI document (scripts/gen-api-types.sh), so a call to a path, method,
// parameter or response the API does not have fails the type check.
//
// Requests go to /api on this app's own origin; next.config.ts forwards them
// to the back end in development.
export const api = createClient<paths>({ baseUrl: '/api' })

// The endpoints whose 401 is an answer for the caller to read, not a sign
// that the session has ended: the current-user endpoint answers 401 to a
// visitor who is not signed in, and sign-in answers 401 to a wrong email or
// password.
const UNAUTHORIZED_IS_AN_ANSWER: ReadonlySet<keyof paths> = new Set([
  '/auth/me',
  '/auth/sign-in',
] as const)

// Any other call that the API answers with 401 sends the visitor to sign in,
// and back to the page they were on afterwards. It is a full page load (see
// page-load.ts), so nothing the app held for the account whose session
// ended survives it.
export const signInOnUnauthorized: Middleware = {
  onResponse({ schemaPath, response }) {
    if (response.status !== 401) return
    if (UNAUTHORIZED_IS_AN_ANSWER.has(schemaPath as keyof paths)) return
    loadPage(authPagePath('/sign-in', currentPath()))
  },
}

api.use(signInOnUnauthorized)
