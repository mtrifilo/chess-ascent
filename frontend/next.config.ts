import type { NextConfig } from 'next'

// The back end's address, as `dotnet run` serves it unless told otherwise.
// .env.example shows how to point the development server elsewhere.
const apiUrl = process.env.CHESS_ASCENT_API_URL ?? 'http://localhost:5100'

const nextConfig: NextConfig = {
  // In development the browser calls the API at this app's own origin, under
  // /api, and the development server forwards each request to the back end
  // with the prefix removed. So the browser sees one origin: no cross-origin
  // requests, and cookies the API sets belong to this app. `next build` reads
  // this too, and gets no rewrite: how a deployment reaches the API is
  // decided with the deployment.
  async rewrites() {
    if (process.env.NODE_ENV !== 'development') {
      return []
    }
    return [{ source: '/api/:path*', destination: `${apiUrl}/:path*` }]
  },
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

export default nextConfig
