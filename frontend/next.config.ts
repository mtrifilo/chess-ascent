import type { NextConfig } from 'next'
import { PHASE_DEVELOPMENT_SERVER } from 'next/constants'

const nextConfig: NextConfig = {
  // Cache Components, create-next-app's recommended caching model: data is
  // cached only where code opts in with 'use cache', and a component that
  // reads request data (cookies, uncached fetches) goes inside <Suspense>.
  // See node_modules/next/dist/docs/01-app/01-getting-started/08-caching.md.
  cacheComponents: true,
  // Goes with Cache Components: links prefetch a route's static shell. The
  // same guide says to set it explicitly; leaving it unset logs a warning.
  partialPrefetching: true,
  // Agent instructions for this repository live in the root CLAUDE.md, so
  // `next dev` does not write a frontend/AGENTS.md.
  agentRules: false,
  experimental: {
    // Nothing about this project is sent to outside services by default.
    // agentUpgrade's default ('security') posts the installed Next.js version
    // to the npm registry's advisory endpoint on every `next dev`, and stops
    // agent-run commands when an advisory applies.
    agentUpgrade: false,
    // Already Next.js's default; pinned so a change of default cannot turn on
    // agent-prepared reports to the Next.js team.
    agentFeedback: false,
  },
  turbopack: {
    rules: {
      '*.css': {
        loaders: ['@tailwindcss/turbopack'],
        as: '*.css',
      },
    },
  },
}

// Next.js calls this with the phase, which names the command being run.
// Only the development server (`next dev`) forwards /api: the browser calls
// the API at this app's own origin, under /api, and the server passes each
// request to the back end with the prefix removed, so the browser sees one
// origin, with no cross-origin requests. Every other phase, `next build`
// included, gets no rewrite: how a deployment reaches the API is decided with
// the deployment. The phase is used rather than NODE_ENV because NODE_ENV can
// be preset to "development" for a build, and the rewrite would then be built
// into the production server.
export default function config(phase: string): NextConfig {
  if (phase !== PHASE_DEVELOPMENT_SERVER) {
    return nextConfig
  }

  // The back end's address; the default is the one `dotnet run` serves on.
  // An empty value counts as unset. .env.example shows how to change it.
  const apiUrl = (
    process.env.CHESS_ASCENT_API_URL || 'http://localhost:5100'
  ).replace(/\/+$/, '')

  return {
    ...nextConfig,
    async rewrites() {
      return [{ source: '/api/:path*', destination: `${apiUrl}/:path*` }]
    },
  }
}
