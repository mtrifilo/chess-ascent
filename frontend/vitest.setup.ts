// Adds DOM matchers such as toBeInTheDocument and toHaveClass to expect.
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Testing Library unmounts rendered trees automatically only when the test
// runner exposes a global afterEach. Vitest globals are off here, so each
// test's DOM is cleared explicitly to keep tests independent.
afterEach(() => {
  cleanup()
})

// A browser resolves a relative URL such as '/api/version' against the page;
// Node's Request, which jsdom leaves in place, rejects it. The API client
// (lib/api.ts) builds every call on Request with a relative base URL, so it is
// made to resolve against jsdom's location as a browser would. This runs
// before any test file imports the client, which captures Request then.
class PageRelativeRequest extends Request {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    super(
      typeof input === 'string' ? new URL(input, location.href) : input,
      init
    )
  }
}
globalThis.Request = PageRelativeRequest

// jsdom has no layout, so it has no ResizeObserver either. A stand-in that
// never reports a resize lets components that observe one render; a test
// that needs a resize installs its own.
class InertResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= InertResizeObserver
