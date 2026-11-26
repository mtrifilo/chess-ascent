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
