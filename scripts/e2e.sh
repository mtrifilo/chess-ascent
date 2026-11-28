#!/usr/bin/env bash
# Runs the end-to-end test (frontend/e2e/) against the whole application:
#   1. starts PostgreSQL in a Docker Compose project of its own, on an empty
#      volume;
#   2. builds the API, at the same time;
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
# it must not be one that holds anything else. A run refuses to start while
# another run holds the project, or while the project has a running
# container.
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

compose() {
  CHESS_ASCENT_DB_PORT="$CHESS_ASCENT_E2E_DB_PORT" \
    docker compose --file "$ROOT/docker-compose.yml" --project-name "$PROJECT" "$@"
}

# One run at a time per Compose project: the clean-up below deletes the
# project's database, which would pull it from under another run. Creating a
# directory either succeeds or fails as one step, so of two runs that start
# together only one gets the lock; it is removed when the run ends.
LOCK="${TMPDIR:-/tmp}/chess-ascent-e2e-$PROJECT.lock"
if ! mkdir "$LOCK" 2>/dev/null; then
  echo "e2e: another end-to-end run is using the Compose project $PROJECT ($LOCK exists)." >&2
  echo "e2e: wait for it to end, or set CHESS_ASCENT_E2E_COMPOSE_PROJECT (and the ports) to run beside it." >&2
  echo "e2e: if no run is using it (a run was killed), remove $LOCK and run: docker compose --project-name $PROJECT down --volumes" >&2
  exit 1
fi
trap 'rmdir "$LOCK"' EXIT

# A running container in the project without the lock was left by a run
# that was killed; it is not deleted without being asked.
if [ -n "$(compose ps --quiet)" ]; then
  echo "e2e: the Compose project $PROJECT is already running, probably left by a run that was killed." >&2
  echo "e2e: if nothing is using it, remove it with: docker compose --project-name $PROJECT down --volumes" >&2
  exit 1
fi

# The API reads the engines directory from this variable. An empty one means
# no engine starts, here as on CI, even where the set-up scripts have run.
# It is a new directory that only this user can write to, rather than a
# fixed path, because the API runs whatever program it finds there.
CHESS_ASCENT_ENGINES_DIR="$(mktemp -d "${TMPDIR:-/tmp}/chess-ascent-e2e-engines.XXXXXX")"
export CHESS_ASCENT_ENGINES_DIR

cleanup() {
  # A database still starting, when the build has failed, finishes first.
  wait || true
  compose down --volumes --remove-orphans || true
  rm -rf "$CHESS_ASCENT_ENGINES_DIR"
  rmdir "$LOCK"
}
trap cleanup EXIT

step() {
  echo "e2e: $1 took $((SECONDS - $2))s"
}

# What a stopped earlier run left, such as its volume, goes first. Then the
# database starts while the API builds; neither needs the other.
compose down --volumes --remove-orphans
started=$SECONDS
compose up --detach --wait &
database=$!
dotnet build "$ROOT/backend/src/ChessAscent.Api" --nologo -v:q
step "building the API" "$started"
wait "$database"
step "starting the database and building the API" "$started"

started=$SECONDS
cd "$ROOT/frontend"
"$PLAYWRIGHT" test "$@"
step "the test, with starting and stopping the API and the web app," "$started"
