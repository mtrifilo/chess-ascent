import createClient from 'openapi-fetch'

import type { paths } from '@/types/api'

// The typed client for the back end. `paths` is generated from the API's
// OpenAPI document (scripts/gen-api-types.sh), so a call to a path, method,
// parameter or response the API does not have fails the type check.
//
// Requests go to /api on this app's own origin; next.config.ts forwards them
// to the back end in development.
export const api = createClient<paths>({ baseUrl: '/api' })
