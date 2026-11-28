# Third-party notices

Chess Ascent is licensed under AGPL-3.0-or-later (see `LICENSE`). It builds on the work listed here.

This file records what the project depends on directly: every engine, model, font and asset, and every library that a project file or `package.json` names. Each entry is added in the same pull request that introduces the dependency. Libraries pulled in only by other libraries are not listed here, but the same licence test applies to them: before adding a dependency, check what it brings with it. A component whose licence is not compatible with AGPL-3.0-or-later, or that forbids commercial use, is not added.

Library versions are not repeated here, because they would go stale: `backend/Directory.Packages.props` and `frontend/bun.lock` hold them. Engines, models and assets are pinned by hand, so their versions are recorded below.

## Engines and models

Run as separate programs, or loaded as data. Not linked into Chess Ascent's own code.

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| Maia-3 (`maia3`) | Commit `1e13597c42d4858b7cfd7cfdae01e297263364b2` (package version 0.1.0); the project has no tags or releases | https://github.com/CSSLab/maia3 | AGPL-3.0 (its `LICENSE` file) | The computer opponent: the authors' program, run unmodified as a child process (`backend/src/ChessAscent.Api/Opponents/MaiaOpponent.cs`). `scripts/setup-maia.sh` installs it into the engines directory on each machine; it is not committed or distributed with Chess Ascent |
| Maia-3 5M model weights (`maia3-5m.pt`) | Revision `b6559de2398d7140b985f28fd2c19fb5e47ddabe` of the Hugging Face repository | https://huggingface.co/UofTCSSLab/Maia3-5M | **Not stated.** The model card says "License: CC BY 4.0 (paper); see repo for code/weights license", and the code repository's only licence is AGPL-3.0, which does not mention weights. The card of the largest model in the family, Maia3-79M, says "License: AGPLv3". Read through the card's pointer to the repository, the weights are AGPL-3.0: an inference, not a statement by the authors. The owner has deferred the question to deployment, since the project does not redistribute the weights | Loaded by Maia-3 to choose the opponent's moves. `scripts/setup-maia.sh` downloads them from the authors' repository into the engines directory on each machine; they are not committed or distributed with Chess Ascent |
| Fairy-Stockfish | Release 14 (tag `fairy_sf_14`, 2021-09-10), commit `f3e6969d11d1bec17eba26e7ae0e629ad4af71dd` | https://github.com/fairy-stockfish/Fairy-Stockfish | GPL-3.0-or-later (the headers of its source files; its licence file, `Copying.txt`, is the GPL version 3) | The hint engine: the authors' program, run unmodified as a child process (`backend/src/ChessAscent.Api/Hints/FairyStockfishHintProvider.cs`). `scripts/setup-fairy-stockfish.sh` fetches the source at that commit and builds it in the engines directory on each machine. Where the compiler rejects the flag `-fexperimental-new-pass-manager`, the script removes the one line of the Makefile that adds it; the engine's code is not changed. Neither the source nor the program is committed or distributed with Chess Ascent |

### Maia-3's Python packages

`scripts/setup-maia.sh` installs these into a Python virtual environment inside the engines directory, at the exact versions in `scripts/maia-constraints.txt`. They are what Maia-3 needs to run, and nothing from them is committed or distributed with Chess Ascent. The environment also holds pip, which Python's `venv` module puts there at the version that comes with the Python used (MIT, https://github.com/pypa/pip).

| Component | Version | Source | Licence | How it is used |
|---|---|---|---|---|
| PyTorch (`torch`) | 2.15.0 (on Linux the CPU-only build, `2.15.0+cpu`, from https://download.pytorch.org/whl/cpu) | https://github.com/pytorch/pytorch | BSD-3-Clause for PyTorch itself; the package's metadata lists Apache-2.0, Apache-2.0 WITH LLVM-exception, BSD-2-Clause, BSD-3-Clause, BSL-1.0, ISC, MIT and Zlib for what it bundles | Runs Maia-3's neural network on the CPU |
| python-chess (`chess`) | 1.11.2 | https://github.com/niklasf/python-chess | GPL-3.0-or-later | Maia-3's chess rules: board, legal moves, UCI notation |
| `python-chess` | 1.999 | https://github.com/niklasf/python-chess | GPL-3.0-or-later | The old package name, which Maia-3 asks for; it contains nothing but a dependency on `chess` |
| NumPy (`numpy`) | 2.5.5 | https://github.com/numpy/numpy | BSD-3-Clause, with 0BSD, MIT, Zlib and CC0-1.0 parts | Arrays, for Maia-3 and PyTorch |
| `huggingface-hub` | 2.5.0 | https://github.com/huggingface/huggingface_hub | Apache-2.0 | Downloads the weights at set-up, and finds them in the local cache when Maia-3 starts |
| `hf-xet` | 1.8.0 | https://github.com/huggingface/xet-core | Apache-2.0 | Hugging Face's file-transfer library, used by `huggingface-hub` for the download |
| `httpx2`, `httpcore2` | 2.16.2 | https://github.com/pydantic/httpx2 | BSD-3-Clause | HTTP client used by `huggingface-hub` |
| `anyio` | 4.16.0 | https://github.com/agronholm/anyio | MIT | Used by `httpx2` |
| `h11` | 0.16.0 | https://github.com/python-hyper/h11 | MIT | Used by `httpcore2` |
| `idna` | 3.20 | https://github.com/kjd/idna | BSD-3-Clause | Used by `httpx2` |
| `truststore` | 0.10.5 | https://github.com/sethmlarson/truststore | MIT | Used by `httpx2` and `httpcore2` to check TLS certificates against the operating system's |
| `click` | 8.5.1 | https://github.com/pallets/click | BSD-3-Clause | Used by `huggingface-hub` |
| `filelock` | 4.3.1 | https://github.com/tox-dev/filelock | MIT | Used by `huggingface-hub` and PyTorch |
| `fsspec` | 2026.9.0 | https://github.com/fsspec/filesystem_spec | BSD-3-Clause | Used by `huggingface-hub` and PyTorch |
| `packaging` | 26.4 | https://github.com/pypa/packaging | Apache-2.0 OR BSD-2-Clause | Used by `huggingface-hub` |
| PyYAML (`pyyaml`) | 6.0.4 | https://github.com/yaml/pyyaml | MIT | Used by `huggingface-hub` |
| `tqdm` | 4.70.1 | https://github.com/tqdm/tqdm | MPL-2.0 AND MIT | Used by `huggingface-hub` for download progress |
| `typing-extensions` | 4.16.0 | https://github.com/python/typing_extensions | PSF-2.0 | Used by PyTorch and `huggingface-hub` |
| SymPy (`sympy`) | 1.14.0 | https://github.com/sympy/sympy | BSD-3-Clause (its metadata says "BSD"; its `LICENSE` is the three-clause text) | Used by PyTorch |
| `mpmath` | 1.3.0 | https://github.com/mpmath/mpmath | BSD-3-Clause (its metadata says "BSD"; its `LICENSE` is the three-clause text) | Used by SymPy |
| NetworkX (`networkx`) | 3.7 | https://github.com/networkx/networkx | BSD-3-Clause | Used by PyTorch |
| Jinja (`jinja2`) | 3.1.6 | https://github.com/pallets/jinja | BSD-3-Clause | Used by PyTorch |
| MarkupSafe (`markupsafe`) | 3.0.4 | https://github.com/pallets/markupsafe | BSD-3-Clause | Used by Jinja |
| `setuptools` | 84.2.1 | https://github.com/pypa/setuptools | MIT | Used by PyTorch, and to build Maia-3 and python-chess from source during set-up |

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
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | https://github.com/dotnet/aspnetcore | MIT | Stores ASP.NET Core Identity's accounts (users, password hashes, lockout counters) in the API's EF Core database. It brings in Microsoft.Extensions.Identity.Stores and Microsoft.Extensions.Identity.Core (Identity's account and password logic), Microsoft.AspNetCore.Cryptography.KeyDerivation (the PBKDF2 password hashing), Microsoft.EntityFrameworkCore.Relational and System.ComponentModel.Annotations (all MIT) |
| Microsoft.AspNetCore.Mvc.Testing | https://github.com/dotnet/aspnetcore | MIT | Runs the API in memory for the API tests (`WebApplicationFactory`); test projects only |
| Microsoft.EntityFrameworkCore.Design | https://github.com/dotnet/efcore | MIT | Design-time services that `dotnet ef` loads from the API project to generate migrations; not published with the application. It brings in the Roslyn compiler packages, Microsoft.Build.Framework, Humanizer.Core and Mono.TextTemplating (all MIT) |
| Microsoft.Extensions.ApiDescription.Server | https://github.com/dotnet/aspnetcore | MIT | Build tool that generates the OpenAPI document when `scripts/gen-api-types.sh` asks for it; not part of the application |
| Npgsql.EntityFrameworkCore.PostgreSQL | https://github.com/npgsql/efcore.pg | PostgreSQL | EF Core provider for PostgreSQL. It brings in Npgsql (PostgreSQL licence), the PostgreSQL driver, which the readiness check and registration (to recognise a unique-index violation) also use directly, and Microsoft.EntityFrameworkCore (MIT) |
| Scalar.AspNetCore | https://github.com/scalar/scalar | MIT | The API explorer at `/scalar`, in Development only. The package carries Scalar's API reference web page and its script inside itself and serves them from the API; its telemetry, AI chat, MCP integration and developer toolbar are turned off |
| Testcontainers.PostgreSql | https://github.com/testcontainers/testcontainers-dotnet | MIT | Starts a PostgreSQL container for the API tests; test projects only. It brings in Testcontainers, Docker.DotNet.Enhanced, Docker.DotNet.Enhanced.X509, SSH.NET, BouncyCastle.Cryptography and SharpZipLib (all MIT), and starts the `testcontainers/ryuk` image (MIT, https://github.com/testcontainers/moby-ryuk), which removes the test containers when a run ends |
| xunit.v3 | https://github.com/xunit/xunit | Apache-2.0 | Test framework; test projects only. It brings in Microsoft.Testing.Platform (MIT), the test runner, and with it Microsoft.Testing.Extensions.Telemetry and Microsoft.ApplicationInsights (both MIT), which send usage telemetry to Microsoft on each test run unless `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` or `DOTNET_CLI_TELEMETRY_OPTOUT=1` is set |

## Front-end libraries

Runtime libraries, or code generated from them, ship to the browser or run in the Next.js server. Development tools build, check or test the code and are not part of what is served.

| Component | Source | Licence | How it is used |
|---|---|---|---|
| Chessground (`@lichess-org/chessground`) | https://github.com/lichess-org/chessground | GPL-3.0-or-later | Runtime: the chess board (drawing, drag and tap moves, arrows and square marks), behind `frontend/components/chess-board/chess-board.tsx`, with its base stylesheet; a piece drawn off the board (`chess-piece.tsx` beside it); and its FEN reader and square helpers in `frontend/features/game/`. It has no dependencies of its own |
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
| Lucide (`lucide-react`) | https://github.com/lucide-icons/lucide | ISC; the icons derived from Feather, which include `check`, `chevron-down`, `chevron-right`, `circle` and `circle-alert`, are MIT | Runtime: icons |
| tw-animate-css | https://github.com/Wombosvideo/tw-animate-css | MIT | Runtime: the open and close animations of the dropdown menu, compiled into the served stylesheet |
| TanStack Query (`@tanstack/react-query`) | https://github.com/TanStack/query | MIT | Runtime: fetches and caches server state in the browser, starting with who is signed in (`frontend/features/auth/session.ts`). It brings in `@tanstack/query-core` (MIT) and nothing else |
| openapi-fetch | https://github.com/openapi-ts/openapi-typescript | MIT | Runtime: the typed API client (`frontend/lib/api.ts`). It brings in openapi-typescript-helpers (MIT) |
| openapi-typescript | https://github.com/openapi-ts/openapi-typescript | MIT | Development: generates `frontend/types/api.d.ts` from the API's OpenAPI document. It brings in Redocly's `@redocly/openapi-core` (MIT) and small utilities under MIT, ISC, Apache-2.0 (`yaml-ast-parser`), MIT-or-CC0 (`type-fest`) and Python-2.0 (`argparse`, through `js-yaml`; ESLint already brought it in) |
| `@testing-library/jest-dom` | https://github.com/testing-library/jest-dom | MIT | Development: DOM assertions in component tests (`toBeInTheDocument`, `toHaveClass`) |
| Playwright (`@playwright/test`) | https://github.com/microsoft/playwright | Apache-2.0 | Development: runs the end-to-end test (`frontend/e2e/`, `scripts/e2e.sh`). It brings in `playwright` and `playwright-core` (Apache-2.0), which bundle their own dependencies under MIT, ISC, Apache-2.0, BSD-3-Clause and BlueOak-1.0.0, and `caniuse-lite`'s browser data under CC-BY-4.0 (each package's `ThirdPartyNotices.txt` and `lib/**/*.LICENSE` files list them). `playwright install chromium` downloads Chrome for Testing and its headless shell (Chromium: BSD-3-Clause, with the licences of its bundled parts) at the build the installed version names in `playwright-core/browsers.json`, from Playwright's download servers, into a per-user cache; the browser is not committed or distributed |

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
| cburnett chess pieces | The copy in Chessground's `assets/chessground.cburnett.css`, at the Chessground version in `frontend/bun.lock` | Drawn by Colin M.L. Burnett for Wikimedia Commons (for example https://commons.wikimedia.org/wiki/File:Chess_klt45.svg); Lichess's copy is `public/piece/cburnett` in https://github.com/lichess-org/lila | The author offers a choice of GFDL-1.2-or-later, CC BY-SA 3.0, BSD-3-Clause or GPL-2.0-or-later ("You may select the license of your choice"). Lichess lists the set as GPL-2.0-or-later | The pieces on the board and in the game screen's promotion picker, embedded as SVG in that stylesheet and served with the board |

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
