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

The user, password and database in `docker-compose.yml` only work against that local container. To use another host port, set `CHESS_ASCENT_DB_PORT` before `docker compose up` and give the API a matching connection string in `Database__ConnectionString`. `docker compose down` stops the database; add `--volumes` to delete its data.

Then, from `backend/`:

```sh
dotnet build                                 # build; any warning fails it
dotnet test                                  # run the tests; starts its own PostgreSQL container
dotnet run --project src/ChessAscent.Api     # apply migrations, then serve the API on http://localhost:5100
```

With the API running, `curl http://localhost:5100/health` answers `Healthy`, `curl http://localhost:5100/health/ready` answers `Healthy` while the database is reachable and `Unhealthy` with status 503 when it is not, and `curl http://localhost:5100/version` returns the version and the git commit it was built from.

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

### Chess engines

Nothing yet.

## Repository layout

| Path | Contents |
|---|---|
| `backend/` | The .NET solution: API, chess domain, tests |
| `frontend/` | The Next.js web app |
| `scripts/` | Set-up and code-generation scripts |
| `engines/` | Chess engines and model weights, created locally by the set-up script and not committed |

## Licence

Chess Ascent is free software under the GNU Affero General Public License, version 3 or later. See `LICENSE`. The engines, libraries and assets it uses are listed with their licences in `THIRD-PARTY-NOTICES.md`.
