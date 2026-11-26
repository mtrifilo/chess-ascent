# Third-party notices

Chess Ascent is licensed under AGPL-3.0-or-later (see `LICENSE`). It builds on the work listed here.

This file records what the project depends on directly: every engine, model, font and asset, and every library that a project file or `package.json` names. Each entry is added in the same pull request that introduces the dependency. Libraries pulled in only by other libraries are not listed here, but the same licence test applies to them: before adding a dependency, check what it brings with it. A component whose licence is not compatible with AGPL-3.0-or-later, or that forbids commercial use, is not added.

Library versions are not repeated here, because they would go stale: `backend/Directory.Packages.props` and `frontend/bun.lock` hold them. Engines, models and assets are pinned by hand, so their versions are recorded below.

## Engines and models

Run as separate programs, or loaded as data. Not linked into Chess Ascent's own code.

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| | | | | |

## Back-end libraries

| Component | Source | Licence | How it is used |
|---|---|---|---|
| AwesomeAssertions | https://github.com/AwesomeAssertions/AwesomeAssertions | Apache-2.0 | Assertions in the test projects only |
| ChessRealms.Engine | https://github.com/ChessRealms/Engine | MIT | The rules of chess (legal moves, FEN, SAN, PGN, game endings), used only inside `ChessAscent.Chess` behind its own interface. It has no dependencies of its own |
| Microsoft.AspNetCore.Mvc.Testing | https://github.com/dotnet/aspnetcore | MIT | Runs the API in memory for the API tests (`WebApplicationFactory`); test projects only |
| xunit.v3 | https://github.com/xunit/xunit | Apache-2.0 | Test framework; test projects only. It brings in Microsoft.Testing.Platform (MIT), the test runner, and with it Microsoft.Testing.Extensions.Telemetry and Microsoft.ApplicationInsights (both MIT), which send usage telemetry to Microsoft on each test run unless `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` or `DOTNET_CLI_TELEMETRY_OPTOUT=1` is set |

## Front-end libraries

Runtime libraries, or code generated from them, ship to the browser or run in the Next.js server. Development tools build, check or test the code and are not part of what is served.

| Component | Source | Licence | How it is used |
|---|---|---|---|
| Next.js (`next`) | https://github.com/vercel/next.js | MIT | Runtime: the web framework (App Router, server rendering, build) |
| React (`react`) | https://github.com/react/react | MIT | Runtime: UI library |
| React DOM (`react-dom`) | https://github.com/react/react | MIT | Runtime: renders React to the browser DOM and to HTML on the server |
| Tailwind CSS (`tailwindcss`) | https://github.com/tailwindlabs/tailwindcss | MIT | Runtime: generates the served stylesheet at build time, including its base (preflight) and theme CSS |
| `@tailwindcss/turbopack` | https://github.com/tailwindlabs/tailwindcss | MIT | Development: runs Tailwind inside the Turbopack bundler |
| TypeScript (`typescript`) | https://github.com/microsoft/TypeScript | Apache-2.0 | Development: type checking |
| `@types/node`, `@types/react`, `@types/react-dom` | https://github.com/DefinitelyTyped/DefinitelyTyped | MIT | Development: type definitions |
| ESLint (`eslint`) | https://github.com/eslint/eslint | MIT | Development: linting |
| `eslint-config-next` | https://github.com/vercel/next.js | MIT | Development: the Next.js lint rules |
| `eslint-plugin-jsx-a11y` | https://github.com/jsx-eslint/eslint-plugin-jsx-a11y | MIT | Development: accessibility lint rules (its recommended set) |
| Prettier (`prettier`) | https://github.com/prettier/prettier | MIT | Development: code formatting |
| Vitest (`vitest`) | https://github.com/vitest-dev/vitest | MIT | Development: unit and component test runner |
| Vite (`vite`) | https://github.com/vitejs/vite | MIT | Development: module transformer that Vitest runs on |
| `@vitejs/plugin-react` | https://github.com/vitejs/vite-plugin-react | MIT | Development: compiles JSX for tests |
| jsdom (`jsdom`) | https://github.com/jsdom/jsdom | MIT | Development: browser DOM for component tests |
| Testing Library (`@testing-library/react`, `@testing-library/dom`) | https://github.com/testing-library/react-testing-library, https://github.com/testing-library/dom-testing-library | MIT | Development: renders components and queries them as a user would |

## Fonts, pieces and other assets

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| | | | | |
