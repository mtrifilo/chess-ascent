#!/usr/bin/env bash
# Installs Maia-3, the computer opponent, for the API to run:
#   1. a Python virtual environment, made with a Python 3.12, 3.13 or 3.14
#      already on this machine;
#   2. PyTorch for the CPU only, then Maia-3 at a pinned commit and the exact
#      package versions in scripts/maia-constraints.txt;
#   3. the weights of Maia-3's 5M model, at a pinned revision, into a
#      Hugging Face cache of its own;
#   4. a check that the engine answers a UCI handshake with the network
#      unavailable to it.
# Everything goes under <engines>/maia, where <engines> is
# $CHESS_ASCENT_ENGINES_DIR or, unset, engines/ at the repository root. The
# API reads the same variable. Nothing is installed anywhere else, and no
# Python is installed.
#
# Usage: scripts/setup-maia.sh. Safe to run again: what is in place is kept.
# Needs bash, git and network access; macOS or Linux.
set -euo pipefail

# Pinned versions, each checked against its source on 2026-11-26.
# Maia-3 has no tags or releases; this is the head of its main branch.
MAIA3_COMMIT=1e13597c42d4858b7cfd7cfdae01e297263364b2
# The commit of the weights' Hugging Face repository (UofTCSSLab/Maia3-5M).
# backend/src/ChessAscent.Api/appsettings.json names the same revision.
WEIGHTS_REVISION=b6559de2398d7140b985f28fd2c19fb5e47ddabe
TORCH_VERSION=2.15.0

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONSTRAINTS="$ROOT/scripts/maia-constraints.txt"
ENGINES="${CHESS_ASCENT_ENGINES_DIR:-$ROOT/engines}"
mkdir -p "$ENGINES/maia"
MAIA="$(cd "$ENGINES/maia" && pwd)"
VENV="$MAIA/venv"
PYTHON="$VENV/bin/python"
HF_CACHE="$MAIA/huggingface"

say() { echo "setup-maia: $*"; }
fail() { echo "setup-maia: $*" >&2; exit 1; }

case "$(uname -s)" in
  Darwin) OS=macos ;;
  Linux) OS=linux ;;
  *) fail "this script supports macOS and Linux only" ;;
esac
command -v git >/dev/null || fail "git is needed to fetch Maia-3; install it and run this again"

# 3.12 to 3.14: Maia-3 needs 3.10 or later, and every pinned package has
# wheels for these. 3.13 is the version Maia was measured with, so it is
# tried first.
in_range() { "$1" -c 'import sys; sys.exit(not (3, 12) <= sys.version_info[:2] <= (3, 14))' 2>/dev/null; }

find_python() {
  local candidate
  for candidate in python3.13 python3.12 python3.14 python3; do
    if command -v "$candidate" >/dev/null && in_range "$candidate"; then
      command -v "$candidate"
      return 0
    fi
  done
  return 1
}

# The environment is reused if its interpreter still runs and is in range.
# One made by this script that no longer works (its Python was removed or
# upgraded, say) is made again.
if [ -x "$PYTHON" ] && in_range "$PYTHON"; then
  say "using the existing environment in $VENV"
else
  if [ -e "$VENV" ]; then
    [ -f "$VENV/pyvenv.cfg" ] || fail "$VENV exists but is not a Python environment; move it away and run this again"
    say "the environment in $VENV no longer works; making it again"
    rm -rf "$VENV"
  fi
  BASE_PYTHON="$(find_python)" || fail "no Python 3.12, 3.13 or 3.14 was found on PATH (as python3.13, python3.12, python3.14 or python3); install one and run this again"
  say "making the environment with $BASE_PYTHON ($("$BASE_PYTHON" --version))"
  "$BASE_PYTHON" -m venv "$VENV" \
    || fail "$BASE_PYTHON could not make a virtual environment; on Debian or Ubuntu, install its venv package (python3-venv)"
fi

pip_install() {
  "$PYTHON" -m pip install --disable-pip-version-check --no-input --no-cache-dir "$@"
}

# setuptools first: Maia-3 and python-chess are built from source with
# --no-build-isolation, so that the build uses this pinned setuptools rather
# than whatever version pip would otherwise download for the build alone.
say "installing setuptools"
pip_install --constraint "$CONSTRAINTS" setuptools

# PyTorch on its own, without its dependencies, which come from PyPI with
# the rest below. On Linux the package on PyPI pulls in more than a gigabyte
# of NVIDIA GPU libraries, so it comes from PyTorch's CPU-only index
# instead; the macOS package on PyPI already runs on the CPU only.
say "installing PyTorch $TORCH_VERSION for the CPU"
if [ "$OS" = linux ]; then
  pip_install --no-deps --index-url https://download.pytorch.org/whl/cpu "torch==$TORCH_VERSION"
else
  pip_install --no-deps "torch==$TORCH_VERSION"
fi

say "installing Maia-3 at commit $MAIA3_COMMIT"
pip_install --constraint "$CONSTRAINTS" --no-build-isolation \
  "maia3 @ git+https://github.com/CSSLab/maia3@$MAIA3_COMMIT"
"$PYTHON" -m pip check --disable-pip-version-check >/dev/null \
  || fail "the installed packages do not agree; see \`$PYTHON -m pip check\`"

# Maia's own download tool, which puts the weights where the engine looks for
# them. A revision that is already in the cache is not downloaded again.
say "fetching the 5M model's weights at revision $WEIGHTS_REVISION"
HF_HOME="$HF_CACHE" HF_HUB_DISABLE_TELEMETRY=1 \
  "$PYTHON" -I -m maia3.cache --model maia3-5m --revision "$WEIGHTS_REVISION" >/dev/null

# The handshake is run the way the API runs the engine: the same arguments
# and an environment holding only the variables the API gives it. On top of
# Maia's offline settings, the network is taken away from the process where
# the operating system allows it, so a download attempt would fail.
say "checking that the engine answers with no network"
ENGINE=(
  /usr/bin/env -i "HF_HOME=$HF_CACHE" HF_HUB_OFFLINE=1 OMP_NUM_THREADS=1
  "$PYTHON" -I -m maia3 --model maia3-5m --revision "$WEIGHTS_REVISION"
  --local-files-only --device cpu --use-uci-history
)
if [ "$OS" = macos ] && [ -x /usr/bin/sandbox-exec ]; then
  NO_NETWORK=(/usr/bin/sandbox-exec -p '(version 1) (allow default) (deny network*)')
  how="with network access denied by sandbox-exec"
elif [ "$OS" = linux ] && command -v unshare >/dev/null && unshare --user --map-root-user --net true 2>/dev/null; then
  NO_NETWORK=(unshare --user --map-root-user --net)
  how="in a network namespace with no interfaces"
else
  NO_NETWORK=()
  how="offline by configuration only: this system gave no way to take the network away from it"
fi
reply="$(cd "$MAIA" && printf 'uci\nisready\nquit\n' | ${NO_NETWORK[@]+"${NO_NETWORK[@]}"} "${ENGINE[@]}" 2>/dev/null)" \
  || fail "the engine exited with an error; run it by hand to see why: cd $MAIA && printf 'uci\\nisready\\n' | ${ENGINE[*]}"
grep -qx uciok <<<"$reply" && grep -qx readyok <<<"$reply" \
  || fail "the engine did not complete the UCI handshake"
say "the engine answered uciok and readyok, $how"

say "done: $(du -sh "$MAIA" | cut -f1) in $MAIA"
