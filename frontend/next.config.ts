import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,
  experimental: {
    // Off explicitly so that no agent is told to prepare reports for the
    // Next.js team. It is also inert while telemetry is off, which the
    // package.json scripts ensure.
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
