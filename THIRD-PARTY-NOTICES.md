# Third-party notices

Chess Ascent is licensed under AGPL-3.0-or-later (see `LICENSE`). It builds on the work listed here.

This file records what the project depends on directly: every engine, model, font and asset, and every library that a project file or `package.json` names. Each entry is added in the same pull request that introduces the dependency. Libraries pulled in only by other libraries are not listed here, but the same licence test applies to them: before adding a dependency, check what it brings with it. A component whose licence is not compatible with AGPL-3.0-or-later, or that forbids commercial use, is not added.

Library versions are not repeated here, because they would go stale: `backend/Directory.Packages.props` and `frontend/bun.lock` hold them. Engines, models and assets are pinned by hand, so their versions are recorded below.

## Engines and models

Run as separate programs, or loaded as data. Not linked into Chess Ascent's own code.

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| | | | | |

## Services

Run as separate programs in containers. Not linked into Chess Ascent's own code.

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| PostgreSQL (`postgres` Docker image) | 18.7 | https://www.postgresql.org, image built from https://github.com/docker-library/postgres | PostgreSQL licence; the image's build files are MIT, and it is based on Debian, whose packages carry their own free-software licences | The database: `docker-compose.yml` for development and the API tests' container |

## Back-end libraries

| Component | Source | Licence | How it is used |
|---|---|---|---|
| AwesomeAssertions | https://github.com/AwesomeAssertions/AwesomeAssertions | Apache-2.0 | Assertions in the test projects only |
| ChessRealms.Engine | https://github.com/ChessRealms/Engine | MIT | The rules of chess (legal moves, FEN, SAN, PGN, game endings), used only inside `ChessAscent.Chess` behind its own interface. It has no dependencies of its own |
| dotnet-ef | https://github.com/dotnet/efcore | MIT | Command-line tool that creates and checks migrations, pinned in `backend/dotnet-tools.json`; run by developers, not part of the application |
| Microsoft.AspNetCore.Mvc.Testing | https://github.com/dotnet/aspnetcore | MIT | Runs the API in memory for the API tests (`WebApplicationFactory`); test projects only |
| Microsoft.EntityFrameworkCore.Design | https://github.com/dotnet/efcore | MIT | Design-time services that `dotnet ef` loads from the API project to generate migrations; not published with the application. It brings in the Roslyn compiler packages, Microsoft.Build.Framework, Humanizer.Core and Mono.TextTemplating (all MIT) |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | https://github.com/dotnet/aspnetcore | MIT | The readiness check that the database answers (`AddDbContextCheck`) |
| Npgsql.EntityFrameworkCore.PostgreSQL | https://github.com/npgsql/efcore.pg | PostgreSQL | EF Core provider for PostgreSQL. It brings in Npgsql (PostgreSQL licence), the PostgreSQL driver, and Microsoft.EntityFrameworkCore (MIT) |
| Testcontainers.PostgreSql | https://github.com/testcontainers/testcontainers-dotnet | MIT | Starts a PostgreSQL container for the API tests; test projects only. It brings in Testcontainers, Docker.DotNet.Enhanced, Docker.DotNet.Enhanced.X509, SSH.NET, BouncyCastle.Cryptography and SharpZipLib (all MIT), and starts the `testcontainers/ryuk` image (MIT, https://github.com/testcontainers/moby-ryuk), which removes the test containers when a run ends |
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
