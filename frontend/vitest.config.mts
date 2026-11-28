import react from '@vitejs/plugin-react'
import { configDefaults, defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  resolve: {
    // Resolves the "@/*" alias from tsconfig.json, so tests import modules
    // the same way the app does.
    tsconfigPaths: true,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    // The end-to-end test is Playwright's (playwright.config.ts), run by
    // scripts/e2e.sh.
    exclude: [...configDefaults.exclude, 'e2e/**'],
    // One time zone for every run, whatever the machine's, so a test of a
    // date in the viewer's own zone means the same everywhere. Los Angeles
    // is behind UTC, so a late-evening time there is a different UTC day.
    env: { TZ: 'America/Los_Angeles' },
  },
})
