import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

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
    // One time zone for every run, whatever the machine's, so a test of a
    // date in the viewer's own zone means the same everywhere. Los Angeles
    // is behind UTC, so a late-evening time there is a different UTC day.
    env: { TZ: 'America/Los_Angeles' },
  },
})
