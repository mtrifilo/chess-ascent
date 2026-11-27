import {
  MutationCache,
  QueryCache,
  QueryClient,
  type DefaultOptions,
} from '@tanstack/react-query'

// A query cache whose failures are reported to the browser console. A query
// or mutation that fails in a way no screen describes, such as the API being
// unreachable, otherwise leaves no trace at all, because TanStack Query logs
// nothing by itself. The errors this app throws name the call and the status
// only, never a request body, so no password is written there.
export function createQueryClient(defaultOptions?: DefaultOptions) {
  return new QueryClient({
    defaultOptions,
    queryCache: new QueryCache({ onError: report }),
    mutationCache: new MutationCache({ onError: report }),
  })
}

function report(error: Error) {
  console.error(error)
}
