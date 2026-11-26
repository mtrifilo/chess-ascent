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

Nothing yet.

### Front end

Nothing yet.

### Both together

Nothing yet.
