# Chess Ascent: agent primer

Chess Ascent teaches casual players chess. It is also a learning project: the code is written to be read by an experienced Java developer who is learning C# and .NET. Next.js front end (Bun for installs and scripts, Next.js itself on Node), C#/.NET back end, AGPL-3.0-or-later.

## Where project knowledge lives

Scope, decisions, research and open questions are kept by the maintainer in planning notes outside this repo. They are not public. They are the source of truth for what to build and why; this file covers how to work in the repo. An agent working for the maintainer is told where the notes are by its brief, or by a git-ignored `CLAUDE.local.md` beside this file.

Linear team `CHA` tracks the work. Read a ticket with its comments before starting it.

## Conventions

- **One issue, one pull request.** Branch `CHA-N/short-description`. Commits start with `CHA-N:`. The PR body includes `Closes CHA-N`.
- **Decisions belong to the owner.** If a ticket leaves open what to build (a name, a threshold, wording, a UX pattern), stop and ask. Do not pick.
- **The code teaches.** A comment that teaches a C# or .NET concept starts with `// LEARN:` and is written for an experienced Java developer: say how the construct differs from its Java counterpart, the first time it appears in an area of the code. Every PR lists the concepts it introduces.
- **Ordinary comments explain why, not what,** in the present tense. No history and no ticket narrative in comments.
- **Record every direct dependency.** A new engine, model, font or asset, or a library the project references directly, is added to `THIRD-PARTY-NOTICES.md` with its source and licence in the same PR. It must be compatible with AGPL-3.0-or-later, and so must whatever it pulls in. Nothing that forbids commercial use. If the rights holder does not state a licence, record it as "not stated", say what you infer and why, and stop for the owner's decision.
- **No secrets in the repo.** It is public and its history is permanent. In development, secrets go in `dotnet user-secrets` or a git-ignored `.env`; in deployment, in the host's environment variables. Committed files such as `appsettings*.json` and `.env.example` hold placeholders, or defaults that only work against the local Docker Compose database. Never commit a database dump, a key or a certificate.
- **Run `dotnet format` after scaffolding.** `dotnet new` and `dotnet ef migrations add` write files with a byte-order mark; this repo uses UTF-8 without one (`.editorconfig`), and `dotnet format --verify-no-changes` fails until the mark is removed.
- **Verify versions when you add them.** Pin exact versions. The research in the notes is a dated snapshot.
- **Do not push unless the tests pass and you have run the change yourself.** For an API change that means calling it; for a UI change, using it in a browser.

## Layout

| Path | Contents |
|---|---|
| `backend/` | The .NET solution: `src/ChessAscent.Api` (web API, one folder per feature), `src/ChessAscent.Chess` (chess rules behind our own interface; no web or database references), and `tests/` |
| `frontend/` | The Next.js app: `app/` (routes), `features/` (one folder per feature), `components/` (shared) |
| `scripts/` | Engine set-up and API type generation |
| `engines/` | Engine binaries, the Python environment and model weights. Git-ignored; recreated by the set-up script |

## How to run things

Each ticket that adds a command adds it under the matching heading below.

### Back end

From the repo root, with Docker running:

- `docker compose up -d --wait`: starts PostgreSQL 18 for development on `127.0.0.1:5540`, and returns once it accepts connections. Set `CHESS_ASCENT_DB_PORT` to use another host port, and then point the API at it with `ConnectionStrings__ChessAscent` (below). `docker compose down` stops it and keeps the data; `docker compose down --volumes` deletes the data too.

From `backend/`, with the .NET 11 SDK (`backend/global.json` pins 11.0.100 and accepts later 11.0 feature bands):

- `dotnet build`: builds the solution. Any warning, analyzer finding or code-style violation fails it.
- `dotnet restore --locked-mode`: restores packages only if every project's `packages.lock.json` still matches, and checks each package against the hash recorded there. An ordinary restore (and so `dotnet build`) rewrites a lock file when a package changes: commit it with that change, or CI fails. With the environment variable `CI` set to `true`, as on GitHub's runners, every restore is locked. A restore that resolves packages also checks them, transitive ones included, against NuGet's vulnerability data, and a known vulnerability fails it; in CI, so does failing to download that data (NU1900), which stays a warning locally.
- `dotnet test`: runs every test project on Microsoft.Testing.Platform. Needs Docker: the API tests start their own PostgreSQL container with Testcontainers and never use the Compose database. While they run, Docker publishes those containers' ports (and that of Testcontainers' clean-up container, Ryuk) on every network interface, as Testcontainers does by default; each test database gets a random password. The `dotnet` CLI and the test platform send usage telemetry to Microsoft unless `DOTNET_CLI_TELEMETRY_OPTOUT=1` is set.
- `dotnet format --verify-no-changes`: fails on formatting or code-style drift. `dotnet format` fixes it.
- `dotnet test --project tests/ChessAscent.Chess.Tests --explicit only --filter-trait "Category=Slow" --parallel all`: the slow tier of the rules tests, which `dotnet test` leaves out: perft to depth 5 on the six standard positions, and the check that every unlisted move is refused at every position of the random games (the normal run checks every tenth). `--parallel all` runs them side by side; even so it takes about three and a half minutes on a 10-core machine, most of it perft from Kiwipete.
- `dotnet run --project src/ChessAscent.Api`: serves the API on `http://localhost:5100` in the Development environment. Add `-- --urls http://localhost:<port>` for another port. It applies pending migrations before it serves anything, and exits if it cannot reach the database. It reads the connection string named `ChessAscent` (`ConnectionStrings:ChessAscent`): `appsettings.Development.json` holds the one for the Compose database, and the environment variable `ConnectionStrings__ChessAscent` overrides it. Without one, it exits at start-up with a message naming that variable. Endpoints: `GET /health` (liveness: 200 whenever the process serves, database or not), `GET /health/ready` (readiness: 503 when the database does not accept a connection within 3 seconds, or while a configured kind of engine has no working process), `GET /version`, and the accounts endpoints `POST /auth/register`, `POST /auth/sign-in`, `POST /auth/sign-out` and `GET /auth/me` (cookie session; `Features/Accounts`), and the games endpoints `GET /games`, `POST /games`, `GET /games/{id}` and `POST /games/{id}/moves` (signed-in players only, their own games only; `Features/Games`, whose `GameEndpoints` documents every status a move can get). Request and response bodies write enumerations by camel-case name and read numbers only as JSON numbers (`Json/JsonServices.cs`). Every request with a method other than GET, HEAD, OPTIONS, TRACE or QUERY that a browser reports as started by another origin is refused with 403 (`Security/CrossSiteRequests.cs`), so a new state-changing endpoint is protected without opting in. In Development only, it also serves its OpenAPI document at `/openapi/v1.json` and the Scalar API explorer at `/scalar`.
- Chess engines: the `EnginePool` configuration section lists the kinds of engine the API runs as child processes (`EnginePool:Kinds:<name>:ExecutablePath`, or the environment variable `EnginePool__Kinds__<name>__ExecutablePath`, and so on; `src/ChessAscent.Api/Engine/EnginePoolOptions.cs` lists every setting). Two kinds are configured in `appsettings.json`: `maia`, the computer opponent, with its program, working directory and `HF_HOME` computed from the engines directory by `src/ChessAscent.Api/Opponents/MaiaInstallation.cs`; and `fairy_stockfish`, the hint engine, with its program and working directory computed by `src/ChessAscent.Api/Hints/FairyStockfishInstallation.cs`. Both use `src/ChessAscent.Api/Engine/EngineInstallation.cs`, which finds the engines directory and puts each engine in a directory named after its kind. A kind's name has no hyphen, because it is part of environment variable names, which a shell cannot set with one. A missing, misspelt or invalid setting stops start-up with a message naming it; an engine that will not start does not stop the API, and fails readiness instead. An engine inherits none of the API's environment variables: it gets exactly the names listed in `CopyEnvironment` (with the API's values) and the variables in `SetEnvironment`, which win where both name one. Never log their values. The API tests remove both kinds unless a test keeps one (`ApiFactory.KeptEngineKinds`), so they pass whether or not the engines are installed.
- `scripts/setup-maia.sh` (from the repo root, macOS or Linux): installs Maia-3 into `engines/maia/`, or into `$CHESS_ASCENT_ENGINES_DIR/maia` when that variable is set, which the API reads too. README.md ("Chess engines") says what it installs. Maia's Python package versions are in `scripts/maia-constraints.txt`. The script checks its install with the same command line, environment and paths as the `maia` kind in `appsettings.json` and `MaiaInstallation.cs`, so the model, the weights' revision and every argument are written in both; `MaiaInstallationTests` compares the script's handshake with the kind and fails when they differ. The script also holds the weights' SHA-256 (`WEIGHTS_SHA256`), which changes with the revision. Change them together, with `THIRD-PARTY-NOTICES.md`. The API defaults to the repository's `engines/` only when it runs from its project directory (`ChessAscent.Api.csproj` in the content root); anywhere else it stops at start-up until `CHESS_ASCENT_ENGINES_DIR` (or the kind's paths) is set.
- `dotnet test --project tests/ChessAscent.Api.Tests --explicit only --filter-trait "Category=Maia"`: the contract test against the real Maia, which `dotnet test` leaves out. Needs the set-up script to have run, and Docker.
- `scripts/setup-fairy-stockfish.sh` (from the repo root, macOS or Linux): builds Fairy-Stockfish 14 from source into `engines/fairy_stockfish/`, or `$CHESS_ASCENT_ENGINES_DIR/fairy_stockfish`. README.md ("Chess engines") says what it does. It pins the source by commit (`FAIRY_STOCKFISH_COMMIT`); change that together with `THIRD-PARTY-NOTICES.md`, the version the script checks for (`id name Fairy-Stockfish 14`) and the version README.md names. The contract test below must still pass unchanged: each expected move is far better than any other, so a different hint is a regression to investigate, not test data to update. It checks its build with the same program, directory and empty environment as the `fairy_stockfish` kind, and `FairyStockfishInstallationTests` fails when the two differ.
- `dotnet test --project tests/ChessAscent.Api.Tests --explicit only --filter-trait "Category=FairyStockfish"`: the contract test against the real Fairy-Stockfish, which `dotnet test` leaves out. It asserts the exact hint for three tactical positions. Needs the set-up script to have run, and Docker.
- `dotnet tool restore`: installs `dotnet-ef` at the version in `dotnet-tools.json`. Needed once per clone before the two commands below.
- `dotnet ef migrations add <Name> --project src/ChessAscent.Api --output-dir Database/Migrations`: generates a migration from the change to the model. Run `dotnet format` afterwards.
- `dotnet ef migrations has-pending-model-changes --project src/ChessAscent.Api`: fails if the model has changed without a migration to match.
- `grep -rn "LEARN:" .`: lists the teaching comments.

### Front end

From `frontend/`:

| Command | Does |
|---|---|
| `bun install` | Installs the pinned dependencies from `bun.lock` |
| `bun run dev` | Serves the app at http://127.0.0.1:3000, on this machine only; add `--port <n>` for another port |
| `bun run typecheck` | Generates Next.js route types, then runs `tsc` |
| `bun run lint` | ESLint with the Next.js rules and jsx-a11y's recommended rules; any warning fails it |
| `bun run test` | Vitest, once (component tests in jsdom) |
| `bun run build` | Production build |
| `bun run start` | Serves the production build on all network interfaces, as a host needs |
| `bun run format` / `bun run format:check` | Prettier: rewrite, or check only |
| `bun run api:types` | Runs `scripts/gen-api-types.sh` (see "Both together") |

- Bun installs packages and runs scripts; Next.js and Vitest themselves run on Node. Never add `bun --bun`.
- `bun test` is Bun's own test runner, not this project's: it runs the tests without jsdom and they fail. Use `bun run test`.
- `@types/node` stays on the same minor version as the Node.js in use (24.11, recorded in `frontend/.node-version`; Bun's version is the `packageManager` field of `package.json`), so the type check rejects Node APIs that the runtime lacks.
- Run Next.js through these scripts, not `bunx next`: they set `NEXT_TELEMETRY_DISABLED=1`. `README.md` ("Run locally", "Front end") says what the front end does and does not send to outside services.
- `bun add` pins exact versions (`bunfig.toml`). Record a new direct dependency in `THIRD-PARTY-NOTICES.md`.
- `bun add` and `bun update` pick only versions published at least three days ago (`minimumReleaseAge` in `bunfig.toml`); asking for a newer one fails with "blocked by minimum-release-age". For an urgent security fix, `bun add <package>@<version> --minimum-release-age=0` overrides it for that one command; say so in the PR. An install from an unchanged `bun.lock` is not affected.
- `bun audit`: checks the packages in `bun.lock` against the npm registry's advisories; any advisory fails it.
- `trustedDependencies` in `package.json` lists the packages whose install scripts Bun may run. It is empty: no dependency needs one, and defining the field, even empty, replaces Bun's built-in allowlist of popular packages. Add a package only after reading the script it would run.
- This Next.js version differs from older training data. Read the guide for the installed version in `frontend/node_modules/next/dist/docs/` before writing Next.js code, and heed its deprecation notices.

### Both together

The front end calls the API through types generated from the API's OpenAPI document, so the C# endpoint and response types are the one source of the contract.

- `scripts/gen-api-types.sh` (or `bun run api:types` from `frontend/`): rebuilds the API with build-time OpenAPI generation, writes `backend/openapi/ChessAscent.Api.json` (OpenAPI 3.1), then `frontend/types/api.d.ts` from it with `openapi-typescript`. Both files are committed. It needs the .NET SDK and `bun install`, and no database or running server. Run it in the same PR as any change that alters the document: an endpoint added or removed, a route, parameter or response type changed, a `///` comment on a response type edited, or a hand-written description (below) changed. On a clean tree it leaves no diff. `openapi-typescript` declares a peer dependency on TypeScript 5 while the project uses 6.0; its output was reviewed when it was added, and a regeneration that changes `api.d.ts` without a matching change to the document points at that.
- The build-time generator runs `Program.cs` with a stand-in server. Start-up code that needs the outside world, such as the database migration, checks `ApiDocumentServices.IsBuildTimeGeneration` and skips itself.
- Endpoints that the generator cannot describe from their C# types are added to the document by hand with a document transformer, as `Features/Health/HealthApiDescription.cs` does for `GET /health/ready`.
- `frontend/lib/api.ts` is the typed client (`openapi-fetch`). It calls `/api/...` on the web app's own origin. In development, `next.config.ts` rewrites `/api/*` to the API at `CHESS_ASCENT_API_URL` (default `http://localhost:5100`, the `dotnet run` port), so the browser never calls the API's port directly. Only `next dev` has this rewrite (it checks the Next.js phase, not `NODE_ENV`); `next build` has none. `/api/*` belongs to the back end: an `app/api` route handler would answer before the rewrite, so the front end has none. To change the address, copy `frontend/.env.example` to `frontend/.env.local`, or set the variable in the shell.
- To run both: start the database and `dotnet run --project src/ChessAscent.Api` in `backend/`, then `bun run dev` in `frontend/`. The home page shows the API's readiness and version.

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and every push to `main`, with the tool versions recorded in the repo (`backend/global.json`, `frontend/package.json`, `frontend/.node-version`). Its jobs run the commands above, so to reproduce a job locally, run its commands in order:

| Job | Commands |
|---|---|
| Back end | From `backend/`, with Docker running: `dotnet restore --locked-mode` (then fails if `git status` shows a `packages.lock.json` that is not committed), `dotnet tool restore`, `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`, `dotnet ef migrations has-pending-model-changes --project src/ChessAscent.Api` |
| Front end | From `frontend/`: `bun install --frozen-lockfile`, `bun run format:check`, `bun run typecheck`, `bun run lint`, `bun run test`, `bun run build` |
| API types drift | `bun install --frozen-lockfile` in `frontend/`, then `scripts/gen-api-types.sh`; fails if `git status` then shows any change |
| Secret scanning | `gitleaks git --redact --log-opts="--full-history --diff-filter=tuxdb --remerge-diff HEAD" .` from the repo root, with Gitleaks (https://github.com/gitleaks/gitleaks) at the version the workflow pins and its default rules (CI ignores any `.gitleaks.toml`); fails on any finding in any commit reachable from `HEAD`, merge resolutions included. Add `--verbose` locally to see each finding in context |
| Dependency advisories | `bun audit` from `frontend/`. The back end's packages are checked by the restore in the back-end job |
| Back end, slow rules tests | Pushes to `main` only: the slow-tier command under "Back end" above |

A ticket that adds a command to "How to run things" that CI should also run adds it to the workflow and to this table in the same PR.

When a check fails on something the PR did not change:

- **Secret scanning.** A real credential is revoked or rotated first; deleting the commit does not make it secret again. A false positive, such as a test fixture that looks like a password, gets a `gitleaks:allow` comment at the end of its line before it is first pushed. Once a commit holding it is pushed, the comment no longer helps, because the scan reads every commit: add the finding's fingerprint, `<Commit>:<File>:<RuleID>:<StartLine>` from the line the job prints, to `.gitleaksignore` at the repo root instead. Either way the PR says why.
- **Dependency advisories** (`bun audit`, or NU1901 to NU1904 from restore). Update the affected package, with the override above if the fix is newer than three days. If no fixed version exists, ask the owner before suppressing the advisory (`bun audit --ignore=<CVE>`, or `NuGetAuditSuppress` in a project file).
