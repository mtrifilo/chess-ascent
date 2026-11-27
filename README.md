# Chess Ascent

A web app that teaches casual chess players to play better: practise against a human-like computer opponent, then work through challenges on tactics, openings and strategy, with advice on which moves are best and why.

**Status: early development.** The first release is a full game against a computer opponent at a chosen strength, with accounts, saved games and a hint button. Nothing is playable yet.

## Stack

- Front end: Next.js, with Bun for installs and scripts.
- Back end: C# on .NET, with PostgreSQL.
- Opponent: Maia, a neural network trained to play like people of a given rating.

## Run locally

Not playable yet. Each part gains its instructions as the first release is built.

### Back end

Needs the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0), version 11.0.100 or a later 11.0 release, and Docker.

The API stores its data in PostgreSQL 18 (the `postgres:18.7` image). From the repository root, start it with:

```sh
docker compose up -d --wait                  # PostgreSQL on 127.0.0.1:5540, once it is ready
```

The user, password and database in `docker-compose.yml` only work against that local container. To use another host port, set `CHESS_ASCENT_DB_PORT` before `docker compose up` and give the API a matching connection string in the environment variable `ConnectionStrings__ChessAscent`. `docker compose down` stops the database; add `--volumes` to delete its data.

Then, from `backend/`:

```sh
dotnet build                                 # build; any warning fails it
dotnet test                                  # run the tests; starts its own PostgreSQL container
dotnet run --project src/ChessAscent.Api     # apply migrations, then serve the API on http://localhost:5100
```

The API applies any pending database migrations before it starts serving, so it needs the database at start-up: if it cannot reach it, it exits with the connection error instead of starting. Once it is running, losing the database fails `/health/ready` but not `/health`.

With the API running, `curl http://localhost:5100/health` answers `Healthy`, `curl http://localhost:5100/health/ready` answers `Healthy` while the database is reachable and `Unhealthy` with status 503 when it does not accept a connection within 3 seconds (or while a configured chess engine has no working process), and `curl http://localhost:5100/version` returns the version and the git commit it was built from.

Accounts are under `/auth`. `POST /auth/register` and `POST /auth/sign-in` take a JSON body with `email` and `password` and set the session cookie, `POST /auth/sign-out` clears it, and `GET /auth/me` returns the signed-in account's email, or 401 when nobody is signed in. A request that changes something (any method but GET, HEAD, OPTIONS, TRACE and QUERY) is refused with 403 when the browser reports that a page on another origin started it, a sibling subdomain included; curl sends no such report, so it can call the API directly:

```sh
jar="${TMPDIR:-/tmp}/chess-ascent-cookies.txt"   # outside the repo: it holds a live session
curl -c "$jar" -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"Example-passw0rd"}' http://localhost:5100/auth/register
curl -b "$jar" http://localhost:5100/auth/me
```

The session cookie is encrypted with ASP.NET Core Data Protection keys, which by default live in `~/.aspnet/DataProtection-Keys` on the machine running the API. Deleting them signs everyone out. A deployment must keep them durable, private and shared between instances.

In Development, `http://localhost:5100/scalar` is an API explorer (Scalar) for the API's endpoints, and `http://localhost:5100/openapi/v1.json` is the OpenAPI document it reads. Scalar's page and script are served by the API itself, and its telemetry, AI chat, MCP integration and developer toolbar (whose buttons would upload the document to Scalar) are turned off.

Migrations use the `dotnet-ef` tool, pinned in `backend/dotnet-tools.json`. Run `dotnet tool restore` once, then `dotnet ef migrations add <Name> --project src/ChessAscent.Api --output-dir Database/Migrations` to add one.

The `dotnet` CLI and the test runner send usage telemetry to Microsoft. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` to turn it off.

### Front end

Needs [Bun](https://bun.com) and Node.js 24. Bun installs the packages and runs the scripts; Next.js itself runs on Node.

```sh
cd frontend
bun install
bun run dev
```

The app is then at http://127.0.0.1:3000, reachable from this machine only. To open it from another device on your network, pass this machine's network address, for example `bun run dev --hostname 192.168.1.20`, and browse to that address; the dev server accepts live-reload connections for the address it is bound to. The checks are `bun run typecheck`, `bun run lint`, `bun run test`, `bun run build` and `bun run format:check`.

Nothing about the project is sent to outside services by default:

- The scripts turn off Next.js telemetry (`NEXT_TELEMETRY_DISABLED=1`). Running `next` directly, without the scripts, leaves it on.
- `frontend/bunfig.toml` turns off Bun's crash reports.
- `frontend/next.config.ts` turns off Next.js's security-advisory check (`experimental.agentUpgrade`), which would otherwise send the installed Next.js version to the npm registry, and keeps agent feedback off.

One request cannot be turned off in Next.js 16.4: when a browser first connects to `bun run dev`, the dev server fetches the public list of Next.js release tags from the npm registry, to show whether the installed version is out of date. It sends no project data.

`bun run build` and `bun run dev` also download the app's three fonts (Hanken Grotesk, Schibsted Grotesk and Golos Text) from Google Fonts. The app serves the font files itself, so browsers that open it never contact Google.

### Both together

Start the database and the API as above, then the front end:

```sh
cd frontend
bun run dev
```

The home page at http://127.0.0.1:3000 shows the API's readiness and version. The browser only talks to the web app: in development, the web app forwards every request for `/api/*` to the API, at `http://localhost:5100` unless `CHESS_ASCENT_API_URL` says otherwise. To change it, copy `frontend/.env.example` to `frontend/.env.local` and edit it there.

The front end's API types are generated from the API's own description of itself. After changing an API endpoint, regenerate them (needs the .NET SDK and `bun install`, but no database):

```sh
cd frontend
bun run api:types        # or scripts/gen-api-types.sh from the repository root
```

It rewrites `backend/openapi/ChessAscent.Api.json` (the OpenAPI 3.1 document) and `frontend/types/api.d.ts`; commit both.

### Chess engines

The API runs chess engines as child processes, each kind configured under `EnginePool:Kinds:<name>` in configuration (`backend/src/ChessAscent.Api/Engine/EnginePoolOptions.cs` lists every setting). None is configured yet, and no engine is installed: setting one up comes with the tickets that add the engines.

An engine starts with an environment of its own, not the API's: the API's environment holds secrets such as the database connection string, and an engine is a third-party program. A kind lists the names of the API's variables to copy (`CopyEnvironment`) and the variables to set to given values (`SetEnvironment`); the engine gets exactly those. With neither, its environment is empty. Give the engine program as a full path: a bare name is looked up with the API's own `PATH`, not the engine's.

## Repository layout

| Path | Contents |
|---|---|
| `backend/` | The .NET solution: API, chess domain, tests |
| `frontend/` | The Next.js web app |
| `scripts/` | Set-up and code-generation scripts |
| `engines/` | Chess engines and model weights, created locally by the set-up script and not committed |

## Licence

Chess Ascent is free software under the GNU Affero General Public License, version 3 or later. See `LICENSE`. The engines, libraries and assets it uses are listed with their licences in `THIRD-PARTY-NOTICES.md`.
