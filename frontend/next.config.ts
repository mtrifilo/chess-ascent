import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Cache Components, create-next-app's recommended caching model: data is
  // cached only where code opts in with 'use cache', and a component that
  // reads request data (cookies, uncached fetches) goes inside <Suspense>.
  // See node_modules/next/dist/docs/01-app/01-getting-started/08-caching.md.
  cacheComponents: true,
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
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
