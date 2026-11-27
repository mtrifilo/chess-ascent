#!/usr/bin/env bash
# Regenerates the API contract from the C# code:
#   1. backend/openapi/ChessAscent.Api.json, the OpenAPI 3.1 document, which
#      the build-time generator writes by running the API with a stand-in
#      server, so no server, database or network is needed;
#   2. frontend/types/api.d.ts, the TypeScript types the front end's API
#      client is checked against, from that document.
# Both files are committed. On a clean tree this script leaves no diff; a
# diff means the committed contract is out of date.
#
# Usage: scripts/gen-api-types.sh, or `bun run api:types` from frontend/.
# Needs the .NET SDK and `bun install` in frontend/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOCUMENT="$ROOT/backend/openapi/ChessAscent.Api.json"
TYPES="$ROOT/frontend/types/api.d.ts"

# The document is written to an empty directory and copied into place only
# once it exists, so a failed build leaves the committed files as they were.
# --no-incremental rebuilds the API, because the generator skips its run when
# the assembly is no newer than its last one.
GENERATED="$(mktemp -d -t chess-ascent-openapi-XXXXXX)"
trap 'rm -rf "$GENERATED"' EXIT
dotnet build "$ROOT/backend/src/ChessAscent.Api" --no-incremental --nologo -v:q \
  -p:OpenApiGenerateDocumentsOnBuild=true \
  -p:OpenApiDocumentsDirectory="$GENERATED"

if [ ! -s "$GENERATED/ChessAscent.Api.json" ]; then
  echo "gen-api-types: the build wrote no OpenAPI document" >&2
  exit 1
fi
mkdir -p "$(dirname "$DOCUMENT")"
cp "$GENERATED/ChessAscent.Api.json" "$DOCUMENT"

cd "$ROOT/frontend"
node_modules/.bin/openapi-typescript "$DOCUMENT" --output "$TYPES"
# The generator's own formatting differs from the repo's Prettier settings,
# which `bun run format:check` enforces on every file.
node_modules/.bin/prettier --write --log-level warn "$TYPES"
echo "gen-api-types: wrote $DOCUMENT and $TYPES"
