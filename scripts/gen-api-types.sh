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

OPENAPI_TYPESCRIPT="$ROOT/frontend/node_modules/.bin/openapi-typescript"
PRETTIER="$ROOT/frontend/node_modules/.bin/prettier"
for tool in "$OPENAPI_TYPESCRIPT" "$PRETTIER"; do
  if [ ! -x "$tool" ]; then
    echo "gen-api-types: $tool is missing; run \`bun install\` in frontend/ first" >&2
    exit 1
  fi
done

# Both files are generated in an empty directory and copied into place only
# once every step has succeeded, so a failure leaves the committed files as
# they were, and never one updated without the other.
GENERATED="$(mktemp -d "${TMPDIR:-/tmp}/chess-ascent-openapi.XXXXXX")"
trap 'rm -rf "$GENERATED"' EXIT

# --no-incremental rebuilds the API, because the generator skips its run when
# the assembly is no newer than its last one.
dotnet build "$ROOT/backend/src/ChessAscent.Api" --no-incremental --nologo -v:q \
  -p:OpenApiGenerateDocumentsOnBuild=true \
  -p:OpenApiDocumentsDirectory="$GENERATED"

if [ ! -s "$GENERATED/ChessAscent.Api.json" ]; then
  echo "gen-api-types: the build wrote no OpenAPI document" >&2
  exit 1
fi
# The generator ends the file without a newline; .editorconfig asks for one.
printf '\n' >>"$GENERATED/ChessAscent.Api.json"

"$OPENAPI_TYPESCRIPT" "$GENERATED/ChessAscent.Api.json" --output "$GENERATED/api.d.ts"
# The generator's own formatting differs from the repo's Prettier settings,
# which `bun run format:check` enforces. Prettier reads its settings for the
# path the file will have, not the temporary one.
"$PRETTIER" --stdin-filepath "$TYPES" <"$GENERATED/api.d.ts" >"$GENERATED/api.formatted.d.ts"

mkdir -p "$(dirname "$DOCUMENT")" "$(dirname "$TYPES")"
cp "$GENERATED/ChessAscent.Api.json" "$DOCUMENT"
cp "$GENERATED/api.formatted.d.ts" "$TYPES"
echo "gen-api-types: wrote $DOCUMENT and $TYPES"
