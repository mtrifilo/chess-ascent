#!/usr/bin/env bash
# Installs Fairy-Stockfish 14, the hint engine, for the API to run:
#   1. its source, at the commit that the release tag fairy_sf_14 names,
#      fetched with git and checked against that commit;
#   2. a build of it for this machine, with the compiler already on it;
#   3. a check that the new program answers a UCI handshake, started as the
#      API starts it;
#   4. only then, the program in place as <engines>/fairy_stockfish/fairy-stockfish.
# Everything goes under <engines>/fairy_stockfish, where <engines> is
# $CHESS_ASCENT_ENGINES_DIR (a full path) or, unset, engines/ at the
# repository root. The API reads the same variable. Nothing is installed
# anywhere else, and no compiler or other tool is installed.
#
# The project publishes no macOS program, and its Linux programs are built
# for x86-64 only, so this builds from source everywhere: one way to
# install, checked against one commit.
#
# Usage: scripts/setup-fairy-stockfish.sh. Safe to run again: the source is
# kept once it is at the pinned commit, and the program is built again.
# Needs bash, git, make and a C++ compiler (on macOS, the Command Line
# Tools; on Linux, g++); network access only while the source is fetched.
set -euo pipefail

# The release tag fairy_sf_14 ("Fairy-Stockfish 14", 2021-09-10), still the
# latest release, names this commit; checked against the project's
# repository on 2026-11-27.
FAIRY_STOCKFISH_COMMIT=f3e6969d11d1bec17eba26e7ae0e629ad4af71dd
REPOSITORY=https://github.com/fairy-stockfish/Fairy-Stockfish.git

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENGINES="${CHESS_ASCENT_ENGINES_DIR:-$ROOT/engines}"

say() { echo "setup-fairy-stockfish: $*"; }
fail() { echo "setup-fairy-stockfish: $*" >&2; exit 1; }

# Git's own variables could point it at another repository (GIT_DIR, say,
# when this runs from a git hook) or change how it reads one; all of them
# are dropped. The user's git configuration, which may hold a proxy, is
# kept: what is fetched is checked against the commit whatever its source.
for variable in $(compgen -e); do
  case "$variable" in
    GIT_*) unset "$variable" ;;
  esac
done

# A relative path would be read against this shell's directory here, and
# against the API's working directory by the API, which differ.
case "$ENGINES" in
  /*) ;;
  *) fail "CHESS_ASCENT_ENGINES_DIR must be a full path, not $ENGINES" ;;
esac

# The build flags for this machine: ARCH names the instruction set in the
# project's Makefile (`make help` lists them), COMP the compiler family.
case "$(uname -s)" in
  Darwin)
    # Without the Command Line Tools, git, make and clang++ are stubs that
    # offer to install them, so they are looked for first.
    xcode-select -p >/dev/null 2>&1 \
      || fail "the Command Line Tools are needed to build Fairy-Stockfish; install them with \`xcode-select --install\` and run this again"
    COMP=clang
    CXX=clang++
    # The hardware, which a shell running under Rosetta reports truly.
    if [ "$(/usr/sbin/sysctl -n hw.optional.arm64 2>/dev/null)" = 1 ]; then
      ARCH=apple-silicon
    else
      ARCH=x86-64-modern
    fi
    ;;
  Linux)
    COMP=gcc
    CXX=g++
    case "$(uname -m)" in
      x86_64)
        # x86-64-modern, the Makefile's default, needs SSE4.1 and POPCNT;
        # plain x86-64 runs on any x86-64 processor.
        if grep -qw popcnt /proc/cpuinfo && grep -qw sse4_1 /proc/cpuinfo; then
          ARCH=x86-64-modern
        else
          ARCH=x86-64
        fi
        ;;
      aarch64 | arm64) ARCH=armv8 ;;
      *) fail "this script builds for x86-64 and 64-bit ARM only, not $(uname -m)" ;;
    esac
    ;;
  *) fail "this script supports macOS and Linux only" ;;
esac
for tool in git make "$CXX"; do
  command -v "$tool" >/dev/null || fail "$tool is needed to build Fairy-Stockfish; install it and run this again"
done

mkdir -p "$ENGINES/fairy_stockfish"
FAIRY="$(cd "$ENGINES/fairy_stockfish" && pwd)"
SOURCE="$FAIRY/source"
PROGRAM="$FAIRY/fairy-stockfish"
BUILD_LOG="$FAIRY/build.log"

# Git is always given the repository explicitly. Left to find it, git in a
# directory that is not a repository looks in the directories above, which
# for the default engines directory is Chess Ascent's own checkout.
source_git() { git --git-dir="$SOURCE/.git" --work-tree="$SOURCE" "$@"; }

# Whatever a run leaves half-made is removed when it ends: a fetch in
# progress, or a new program not yet checked. Signals are trapped because,
# sent to this script alone while git or make runs in the foreground, an
# untrapped TERM or HUP makes bash run the EXIT trap at once and leave that
# command running, still writing, and an untrapped INT makes bash wait for
# the command and then carry on if it exits normally. A trapped signal makes
# bash wait for the command, then run the trap. Each signal has a trap of its
# own so that it can clean up and then raise that same signal again, with
# its default action, so that the script dies by it and a command line
# around it stops too.
FETCHING=""
CANDIDATE=""
remove_half_made() {
  [ -z "$FETCHING" ] || rm -rf "$FETCHING"
  [ -z "$CANDIDATE" ] || rm -f "$CANDIDATE"
}
trap remove_half_made EXIT
for signal in INT TERM HUP; do
  trap "remove_half_made; trap - $signal EXIT; kill -s $signal \$\$" "$signal"
done

if [ -d "$SOURCE/.git" ] && [ "$(source_git rev-parse HEAD 2>/dev/null)" = "$FAIRY_STOCKFISH_COMMIT" ]; then
  say "using the source in $SOURCE, at commit $FAIRY_STOCKFISH_COMMIT"
else
  if [ -e "$SOURCE" ] && [ ! -d "$SOURCE/.git" ]; then
    fail "$SOURCE exists but is not a git checkout; move it away and run this again"
  fi
  # Fetched beside the old checkout, which is replaced only once the new one
  # is complete and checked, so that a failed fetch leaves it as it was.
  say "fetching the source at commit $FAIRY_STOCKFISH_COMMIT"
  FETCHING="$(mktemp -d "$FAIRY/source.XXXXXX")"
  fetching_git() { git --git-dir="$FETCHING/.git" --work-tree="$FETCHING" "$@"; }
  git init --quiet "$FETCHING"
  # Only that one commit, by its id, without the project's history.
  fetching_git fetch --quiet --depth 1 "$REPOSITORY" "$FAIRY_STOCKFISH_COMMIT" \
    || fail "could not fetch commit $FAIRY_STOCKFISH_COMMIT from $REPOSITORY (see above)"
  fetching_git -c advice.detachedHead=false checkout --quiet --detach FETCH_HEAD
  # Git names every commit by the hash of its content, and checks each
  # object it receives against its name, so a checkout at this id holds
  # exactly the source that the commit recorded.
  actual="$(fetching_git rev-parse HEAD)"
  [ "$actual" = "$FAIRY_STOCKFISH_COMMIT" ] \
    || fail "the source fetched is at commit $actual, not $FAIRY_STOCKFISH_COMMIT"
  rm -rf "$SOURCE"
  mv "$FETCHING" "$SOURCE"
  FETCHING=""
fi

# Every tracked file is put back as the commit has it, and everything else
# (the previous build, an earlier run's change to the Makefile) is removed,
# so that each build starts from the pinned source alone.
source_git reset --quiet --hard "$FAIRY_STOCKFISH_COMMIT"
source_git clean --quiet -d --force -x

# For clang, the Makefile adds -fexperimental-new-pass-manager, which newer
# versions of clang (Apple clang 17, for one) reject as an unknown argument.
# Where the compiler rejects it, that one line is removed; the engine's own
# code is not changed.
PASS_MANAGER_LINE=$'\t\tCXXFLAGS += -fexperimental-new-pass-manager'
if [ "$COMP" = clang ] && ! printf 'int main() { return 0; }\n' | "$CXX" -fexperimental-new-pass-manager -fsyntax-only -x c++ - 2>/dev/null; then
  say "$CXX rejects -fexperimental-new-pass-manager; removing it from the Makefile"
  grep -qxF "$PASS_MANAGER_LINE" "$SOURCE/src/Makefile" \
    || fail "the Makefile at $SOURCE/src/Makefile does not add -fexperimental-new-pass-manager where expected"
  grep -vxF "$PASS_MANAGER_LINE" "$SOURCE/src/Makefile" >"$SOURCE/src/Makefile.edited" || true
  mv "$SOURCE/src/Makefile.edited" "$SOURCE/src/Makefile"
  [ "$(source_git diff --numstat)" = $'0\t1\tsrc/Makefile' ] \
    || fail "removing the flag changed more than that one line of the Makefile"
fi

# make reads every environment variable as a variable of the Makefile, and
# this Makefile has many (debug, optimize, nnue, CXXFLAGS, ...), so the
# build gets only PATH, to find the compiler, and TMPDIR, where it writes.
# The default, nnue=no, builds no neural network in and downloads none: the
# engine plays with its classical evaluation.
JOBS="$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 2)"
say "building for ARCH=$ARCH with $CXX ($JOBS jobs; the log is $BUILD_LOG)"
if ! /usr/bin/env -i PATH="$PATH" TMPDIR="${TMPDIR:-/tmp}" \
  make -C "$SOURCE/src" -j "$JOBS" build ARCH="$ARCH" COMP="$COMP" >"$BUILD_LOG" 2>&1; then
  tail -n 20 "$BUILD_LOG" >&2
  fail "the build failed; the last lines of $BUILD_LOG are above"
fi

# The new program is checked beside the one in place, which it replaces
# only once it passes; a program that fails is removed and the previous one
# kept. The handshake is run the way the API runs the engine: the program
# itself, with no arguments, in its own directory, and with an empty
# environment (backend/src/ChessAscent.Api/appsettings.json).
CANDIDATE="$PROGRAM.new"
# A new file each time, never one written over: a program that was run is
# not rewritten in place.
rm -f "$CANDIDATE"
cp "$SOURCE/src/stockfish" "$CANDIDATE"
rejected() { fail "$1; the program already in $FAIRY, if any, was kept"; }
say "checking that the engine answers"
ENGINE=(/usr/bin/env -i "$CANDIDATE")
reply="$(cd "$FAIRY" && printf 'uci\nisready\nquit\n' | "${ENGINE[@]}" 2>&1)" \
  || rejected "the engine exited with an error; run it by hand to see why: cd $(printf %q "$FAIRY") && /usr/bin/env -i $(printf %q "$SOURCE/src/stockfish")"
grep -qx 'id name Fairy-Stockfish 14' <<<"$reply" \
  || rejected "the engine did not name itself as Fairy-Stockfish 14"
grep -qx uciok <<<"$reply" && grep -qx readyok <<<"$reply" \
  || rejected "the engine did not complete the UCI handshake"
say "the engine answered uciok and readyok"

# Renamed into place: an API running the previous program keeps it, and
# never sees a half-written file.
mv -f "$CANDIDATE" "$PROGRAM"
CANDIDATE=""

say "done: $(du -sh "$FAIRY" | cut -f1) in $FAIRY"
