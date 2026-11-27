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
| Microsoft.AspNetCore.OpenApi | https://github.com/dotnet/aspnetcore | MIT | Describes the API's endpoints as an OpenAPI document, served at `/openapi/v1.json` in Development. It brings in Microsoft.OpenApi (MIT, https://github.com/microsoft/OpenAPI.NET), the document model |
| Microsoft.AspNetCore.Mvc.Testing | https://github.com/dotnet/aspnetcore | MIT | Runs the API in memory for the API tests (`WebApplicationFactory`); test projects only |
| Microsoft.EntityFrameworkCore.Design | https://github.com/dotnet/efcore | MIT | Design-time services that `dotnet ef` loads from the API project to generate migrations; not published with the application. It brings in the Roslyn compiler packages, Microsoft.Build.Framework, Humanizer.Core and Mono.TextTemplating (all MIT) |
| Microsoft.Extensions.ApiDescription.Server | https://github.com/dotnet/aspnetcore | MIT | Build tool that generates the OpenAPI document when `scripts/gen-api-types.sh` asks for it; not part of the application |
| Npgsql.EntityFrameworkCore.PostgreSQL | https://github.com/npgsql/efcore.pg | PostgreSQL | EF Core provider for PostgreSQL. It brings in Npgsql (PostgreSQL licence), the PostgreSQL driver, which the readiness check also uses directly, and Microsoft.EntityFrameworkCore (MIT) |
| Scalar.AspNetCore | https://github.com/scalar/scalar | MIT | The API explorer at `/scalar`, in Development only. The package carries Scalar's API reference web page and its script inside itself and serves them from the API; its telemetry, AI chat, MCP integration and developer toolbar are turned off |
| Testcontainers.PostgreSql | https://github.com/testcontainers/testcontainers-dotnet | MIT | Starts a PostgreSQL container for the API tests; test projects only. It brings in Testcontainers, Docker.DotNet.Enhanced, Docker.DotNet.Enhanced.X509, SSH.NET, BouncyCastle.Cryptography and SharpZipLib (all MIT), and starts the `testcontainers/ryuk` image (MIT, https://github.com/testcontainers/moby-ryuk), which removes the test containers when a run ends |
| xunit.v3 | https://github.com/xunit/xunit | Apache-2.0 | Test framework; test projects only. It brings in Microsoft.Testing.Platform (MIT), the test runner, and with it Microsoft.Testing.Extensions.Telemetry and Microsoft.ApplicationInsights (both MIT), which send usage telemetry to Microsoft on each test run unless `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` or `DOTNET_CLI_TELEMETRY_OPTOUT=1` is set |

## Front-end libraries

Runtime libraries, or code generated from them, ship to the browser or run in the Next.js server. Development tools build, check or test the code and are not part of what is served.

| Component | Source | Licence | How it is used |
|---|---|---|---|
| Chessground (`@lichess-org/chessground`) | https://github.com/lichess-org/chessground | GPL-3.0-or-later | Runtime: the chess board (drawing, drag and tap moves, arrows and square marks), behind `frontend/components/chess-board/chess-board.tsx`, with its base stylesheet. It has no dependencies of its own |
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
| Radix UI Primitives (`@radix-ui/react-dropdown-menu`, `@radix-ui/react-label`, `@radix-ui/react-slot`) | https://github.com/radix-ui/primitives | MIT | Runtime: accessible behaviour for the dropdown menu, label and `asChild` button |
| class-variance-authority | https://github.com/joe-bell/cva | Apache-2.0 | Runtime: the variant classes of the button, badge and alert |
| clsx | https://github.com/lukeed/clsx | MIT | Runtime: joins class names (`cn` in `frontend/lib/utils.ts`) |
| tailwind-merge | https://github.com/dcastil/tailwind-merge | MIT | Runtime: resolves conflicting Tailwind classes (`cn`) |
| Lucide (`lucide-react`) | https://github.com/lucide-icons/lucide | ISC; the icons derived from Feather, which include `check`, `chevron-down`, `chevron-right` and `circle`, are MIT | Runtime: icons |
| tw-animate-css | https://github.com/Wombosvideo/tw-animate-css | MIT | Runtime: the open and close animations of the dropdown menu, compiled into the served stylesheet |
| openapi-fetch | https://github.com/openapi-ts/openapi-typescript | MIT | Runtime: the typed API client (`frontend/lib/api.ts`). It brings in openapi-typescript-helpers (MIT) |
| openapi-typescript | https://github.com/openapi-ts/openapi-typescript | MIT | Development: generates `frontend/types/api.d.ts` from the API's OpenAPI document. It brings in Redocly's `@redocly/openapi-core` (MIT) and small utilities under MIT, ISC, Apache-2.0 (`yaml-ast-parser`), MIT-or-CC0 (`type-fest`) and Python-2.0 (`argparse`, through `js-yaml`; ESLint already brought it in) |
| `@testing-library/jest-dom` | https://github.com/testing-library/jest-dom | MIT | Development: DOM assertions in component tests (`toBeInTheDocument`, `toHaveClass`) |

## Code adapted from other projects

| Component | Source | Licence | How it is used |
|---|---|---|---|
| Psychic Homily's design tokens and UI components | https://github.com/mtrifilo/psychic-homily-web (`frontend/app/globals.css`, `frontend/app/layout.tsx`, `frontend/components/ui/`, `frontend/lib/utils.ts`) | MIT, Copyright (c) 2025-2026 Psychic Homily, as stated in the "License" section of its README. Notice below | The colour, radius and font tokens and base styles in `frontend/app/globals.css`, the font set-up in `frontend/app/layout.tsx`, and the components and tests in `frontend/components/ui/`, ported with small changes; `frontend/lib/utils.ts` is adapted from it |
| shadcn/ui | https://github.com/shadcn-ui/ui | MIT, Copyright (c) 2023 shadcn. Notice below | Psychic Homily's components in `frontend/components/ui/` are derived from shadcn/ui's |

## Fonts, pieces and other assets

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| Hanken Grotesk | Whatever Google Fonts serves at build time; `next/font/google` does not pin a version | https://github.com/marcologous/hanken-grotesk, through Google Fonts | OFL-1.1 | Display type. Downloaded at build time and served by the app |
| Schibsted Grotesk | As above | https://github.com/schibsted/schibsted-grotesk, through Google Fonts | OFL-1.1 | Body and interface type. Downloaded at build time and served by the app |
| Golos Text | As above | https://github.com/googlefonts/golos-text, through Google Fonts | OFL-1.1 | Data type (counts, times, dates). Downloaded at build time and served by the app |
| cburnett chess pieces | The copy in Chessground's `assets/chessground.cburnett.css`, at the Chessground version in `frontend/bun.lock` | Drawn by Colin M.L. Burnett for Wikimedia Commons (for example https://commons.wikimedia.org/wiki/File:Chess_klt45.svg); Lichess's copy is `public/piece/cburnett` in https://github.com/lichess-org/lila | The author offers a choice of GFDL-1.2-or-later, CC BY-SA 3.0, BSD-3-Clause or GPL-2.0-or-later ("You may select the license of your choice"). Lichess lists the set as GPL-2.0-or-later | The pieces on the board, embedded as SVG in that stylesheet and served with the board |

## Licence notices for adapted code

The MIT licence asks that its copyright and permission notice travel with copies of the code, so the notices for the code adapted above are reproduced here.

### Psychic Homily

```text
MIT License

Copyright (c) 2025-2026 Psychic Homily

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### shadcn/ui

```text
MIT License

Copyright (c) 2023 shadcn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
