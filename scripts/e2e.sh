#!/usr/bin/env bash
# Runs the end-to-end test (frontend/e2e/) against the whole application:
#   1. starts PostgreSQL in a Docker Compose project of its own, on an empty
#      volume;
#   2. builds the API;
#   3. runs Playwright, which starts the API and the web app's development
#      server (frontend/playwright.config.ts), runs the test in Chromium,
#      and stops both;
#   4. removes the database and its volume, whether the test passed or not.
# The computer opponent is the API's first-legal-move stand-in, so no engine
# is needed.
#
# Usage: scripts/e2e.sh [Playwright options], or `bun run e2e` from frontend/.
# Needs Docker, the .NET SDK, `bun install` in frontend/ and Playwright's
# Chromium (`bun run playwright install chromium` in frontend/ once).
#
# Its ports and Compose project differ from the development defaults (web
# app 3000, API 5100, database 5540), so it runs beside a developer's own
# copies without touching them; set these to move them:
#   CHESS_ASCENT_E2E_WEB_PORT        web app, default 3300
#   CHESS_ASCENT_E2E_API_PORT        API, default 5300
#   CHESS_ASCENT_E2E_DB_PORT         PostgreSQL on 127.0.0.1, default 5640
#   CHESS_ASCENT_E2E_COMPOSE_PROJECT Compose project, default chess-ascent-e2e
# The Compose project's volume is deleted at the start and at the end, so
# it must not be one that holds anything else.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export CHESS_ASCENT_E2E_WEB_PORT="${CHESS_ASCENT_E2E_WEB_PORT:-3300}"
export CHESS_ASCENT_E2E_API_PORT="${CHESS_ASCENT_E2E_API_PORT:-5300}"
export CHESS_ASCENT_E2E_DB_PORT="${CHESS_ASCENT_E2E_DB_PORT:-5640}"
PROJECT="${CHESS_ASCENT_E2E_COMPOSE_PROJECT:-chess-ascent-e2e}"

PLAYWRIGHT="$ROOT/frontend/node_modules/.bin/playwright"
if [ ! -x "$PLAYWRIGHT" ]; then
  echo "e2e: $PLAYWRIGHT is missing; run \`bun install\` in frontend/ first" >&2
  exit 1
fi

# The API reads the engines directory from this variable. An empty one means
# no engine starts, here as on CI, even where the set-up scripts have run.
CHESS_ASCENT_ENGINES_DIR="$(mktemp -d "${TMPDIR:-/tmp}/chess-ascent-e2e-engines.XXXXXX")"
export CHESS_ASCENT_ENGINES_DIR

compose() {
  CHESS_ASCENT_DB_PORT="$CHESS_ASCENT_E2E_DB_PORT" \
    docker compose --file "$ROOT/docker-compose.yml" --project-name "$PROJECT" "$@"
}

cleanup() {
  compose down --volumes --remove-orphans || true
  rm -rf "$CHESS_ASCENT_ENGINES_DIR"
}
trap cleanup EXIT

step() {
  echo "e2e: $1 took $((SECONDS - $2))s"
}

started=$SECONDS
compose down --volumes --remove-orphans
compose up --detach --wait
step "starting the database" "$started"

started=$SECONDS
dotnet build "$ROOT/backend/src/ChessAscent.Api" --nologo -v:q
step "building the API" "$started"

started=$SECONDS
cd "$ROOT/frontend"
"$PLAYWRIGHT" test "$@"
step "the test, with starting and stopping the API and the web app," "$started"
