import { defineConfig, devices } from '@playwright/test'

// The end-to-end test. scripts/e2e.sh runs it: it starts the database, sets
// the ports below and builds the API, and Playwright then starts the API and
// the web app's development server, runs the test, and stops both.

const apiPort = port('CHESS_ASCENT_E2E_API_PORT')
const webPort = port('CHESS_ASCENT_E2E_WEB_PORT')
const databasePort = port('CHESS_ASCENT_E2E_DB_PORT')

// Each server is started for the run and stopped when it ends. Neither is
// reused: if something already answers on its port, the run stops before
// the test, rather than test or change a server it did not start.
const startedHere = {
  reuseExistingServer: false,
  timeout: 120_000,
  gracefulShutdown: { signal: 'SIGTERM', timeout: 10_000 },
} as const

export default defineConfig({
  testDir: './e2e',
  // A retry would hide a test that fails only some of the time.
  retries: 0,
  workers: 1,
  forbidOnly: true,
  // The development server compiles each page the first time it is opened,
  // which on a cold start takes seconds; these limits leave room for that.
  timeout: 120_000,
  expect: { timeout: 30_000 },
  // No HTML report, which would open in the desktop's browser on a failure.
  reporter: 'list',
  use: {
    baseURL: `http://127.0.0.1:${webPort}`,
    // What a failure leaves in test-results/, which CI keeps as an
    // artifact: the trace and a screenshot of the page.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      name: 'API',
      // Built by scripts/e2e.sh beforehand, so the wait below is only for
      // start-up. Without the launch profile, the address and environment
      // are the ones given here.
      command: `dotnet run --no-build --no-launch-profile --project ../backend/src/ChessAscent.Api -- --urls http://127.0.0.1:${apiPort}`,
      // Liveness, not readiness: the engines are configured, but
      // scripts/e2e.sh points their directory at an empty one, so
      // /health/ready answers 503 throughout. Liveness answers once the
      // migrations have run and the API serves.
      url: `http://127.0.0.1:${apiPort}/health`,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__ChessAscent: `Host=127.0.0.1;Port=${databasePort};Database=chess_ascent;Username=chess_ascent;Password=local-development-only`, // gitleaks:allow (the Docker Compose database's, as in appsettings.Development.json)
        // The computer plays the first legal move, so it needs no engine
        // and the test knows its reply (Opponents/OpponentServices.cs).
        Opponent__UseFirstLegalMove: 'true',
        // The API logs to standard output, which Playwright drops; from
        // warnings up it writes to standard error instead, which Playwright
        // shows, so the cause of a failure is in the run's output.
        Logging__Console__LogToStandardErrorThreshold: 'Warning',
      },
      ...startedHere,
    },
    {
      name: 'Web',
      // The development server, whose /api rewrite (next.config.ts) is how
      // the browser reaches the API. Next.js allows one per directory: with
      // a developer's `bun run dev` already running in this frontend/, it
      // refuses to start, and so does the run.
      command: `bun run dev --port ${webPort}`,
      // The first page the test opens, so that it is compiled by the time
      // the test starts.
      url: `http://127.0.0.1:${webPort}/sign-up`,
      env: { CHESS_ASCENT_API_URL: `http://127.0.0.1:${apiPort}` },
      ...startedHere,
    },
  ],
})

function port(name: string): number {
  const value = Number(process.env[name])
  if (!Number.isInteger(value) || value <= 0 || value > 65_535) {
    throw new Error(
      `${name} is not set to a port. Run the end-to-end test with scripts/e2e.sh (bun run e2e from frontend/), which sets it.`
    )
  }
  return value
}
