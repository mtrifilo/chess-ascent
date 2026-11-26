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

Needs the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0), version 11.0.100 or a later 11.0 release. From `backend/`:

```sh
dotnet build                                 # build; any warning fails it
dotnet test                                  # run the tests
dotnet run --project src/ChessAscent.Api     # serve the API on http://localhost:5100
```

With the API running, `curl http://localhost:5100/health` answers `Healthy` and `curl http://localhost:5100/version` returns the version and the git commit it was built from.

The `dotnet` CLI and the test runner send usage telemetry to Microsoft. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` to turn it off.

### Front end

Needs [Bun](https://bun.com) and Node.js 24. Bun installs the packages and runs the scripts; Next.js itself runs on Node.

```sh
cd frontend
bun install
bun run dev
```

The app is then at http://localhost:3000. The checks are `bun run typecheck`, `bun run lint`, `bun run test` and `bun run build`.

The scripts turn off Next.js telemetry (`NEXT_TELEMETRY_DISABLED=1`), and `frontend/bunfig.toml` turns off Bun's crash reports. One outside request remains: outside CI, `next dev` (and `next build`, when run in a terminal) asks the npm registry whether the installed Next.js version has a published security advisory, sending only that version number.

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
