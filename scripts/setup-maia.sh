#!/usr/bin/env bash
# Installs Maia-3, the computer opponent, for the API to run:
#   1. a Python virtual environment, made with a Python 3.12, 3.13 or 3.14
#      already on this machine;
#   2. PyTorch for the CPU only, then Maia-3 at a pinned commit and the exact
#      package versions in scripts/maia-constraints.txt;
#   3. the weights of Maia-3's 5M model, at a pinned revision and checked
#      against their SHA-256, into a Hugging Face cache of its own;
#   4. a check that the engine answers a UCI handshake with the network
#      unavailable to it.
# Everything goes under <engines>/maia, where <engines> is
# $CHESS_ASCENT_ENGINES_DIR (a full path) or, unset, engines/ at the
# repository root. The
# API reads the same variable. Nothing is installed anywhere else, and no
# Python is installed.
#
# Usage: scripts/setup-maia.sh. Safe to run again: what is in place is kept.
# Needs bash, git and network access; macOS 14 or later on Apple Silicon, or
# Linux.
set -euo pipefail

# Pinned versions, each checked against its source on 2026-11-26.
# Maia-3 has no tags or releases; this is the head of its main branch.
MAIA3_COMMIT=1e13597c42d4858b7cfd7cfdae01e297263364b2
# The commit of the weights' Hugging Face repository (UofTCSSLab/Maia3-5M).
# backend/src/ChessAscent.Api/appsettings.json names the same revision.
WEIGHTS_REVISION=b6559de2398d7140b985f28fd2c19fb5e47ddabe
# The SHA-256 of maia3-5m.pt at that revision, as the repository's LFS
# pointer records it; the download is checked against it.
WEIGHTS_SHA256=ba14208b2992d85502f5fb501934abf6aaaeb355e9f3fdf90e326911f562524f

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONSTRAINTS="$ROOT/scripts/maia-constraints.txt"
ENGINES="${CHESS_ASCENT_ENGINES_DIR:-$ROOT/engines}"

say() { echo "setup-maia: $*"; }
fail() { echo "setup-maia: $*" >&2; exit 1; }

# The user's own Python, pip and Hugging Face settings would change what is
# installed and from where: a module path, an extra package index, another
# cache, server, token or offline switch. All of them are dropped, whatever
# their names, and pip is told to read no configuration file.
for variable in $(compgen -e); do
  case "$variable" in
    PYTHON* | PIP_* | HF_* | HUGGINGFACE_* | HUGGING_FACE_* | TRANSFORMERS_*) unset "$variable" ;;
  esac
done
export PIP_CONFIG_FILE=/dev/null

# A relative path would be read against this shell's directory here, and
# against the API's working directory by the API, which differ.
case "$ENGINES" in
  /*) ;;
  *) fail "CHESS_ASCENT_ENGINES_DIR must be a full path, not $ENGINES" ;;
esac
mkdir -p "$ENGINES/maia"
MAIA="$(cd "$ENGINES/maia" && pwd)"
VENV="$MAIA/venv"
PYTHON="$VENV/bin/python"
HF_CACHE="$MAIA/huggingface"

# PyTorch 2.15.0 publishes macOS packages for Apple Silicon and macOS 14 or
# later only, so on macOS the Python must be an Apple Silicon build too (see
# in_range).
case "$(uname -s)" in
  Darwin)
    OS=macos
    # The hardware, which a shell running under Rosetta reports truly.
    [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = 1 ] || fail "on macOS, PyTorch needs an Apple Silicon Mac"
    [ "$(sw_vers -productVersion | cut -d. -f1)" -ge 14 ] || fail "on macOS, PyTorch needs macOS 14 or later"
    ;;
  Linux) OS=linux ;;
  *) fail "this script supports macOS and Linux only" ;;
esac
command -v git >/dev/null || fail "git is needed to fetch Maia-3; install it and run this again"

# 3.12 to 3.14: Maia-3 needs 3.10 or later, and every pinned package has
# wheels for these. 3.13 is the version Maia was measured with, so it is
# tried first. On macOS the interpreter itself must run as arm64: an Intel
# build, under Rosetta, would find no PyTorch package.
in_range() {
  "$1" -I -c 'import platform, sys; sys.exit(not ((3, 12) <= sys.version_info[:2] <= (3, 14) and (sys.platform != "darwin" or platform.machine() == "arm64")))' 2>/dev/null
}

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

# The environment is reused if its interpreter still runs, is in range and
# has pip. One made by this script that does not (its Python was removed or
# upgraded, or making it was cut short) is made again.
if [ -x "$PYTHON" ] && in_range "$PYTHON" && "$PYTHON" -I -m pip --version >/dev/null 2>&1; then
  say "using the existing environment in $VENV"
else
  # Found before anything is removed, so that a run that cannot make a new
  # environment leaves the old one as it was.
  BASE_PYTHON="$(find_python)" || fail "no Python 3.12, 3.13 or 3.14 (on macOS, an Apple Silicon build) was found on PATH as python3.13, python3.12, python3.14 or python3; install one and run this again"
  if [ -e "$VENV" ]; then
    [ -f "$VENV/pyvenv.cfg" ] || fail "$VENV exists but is not a Python environment; move it away and run this again"
    say "the environment in $VENV no longer works; making it again"
    rm -rf "$VENV"
  fi
  say "making the environment with $BASE_PYTHON ($("$BASE_PYTHON" --version))"
  if ! "$BASE_PYTHON" -I -m venv "$VENV"; then
    # A failed attempt can leave an environment without pip behind.
    rm -rf "$VENV"
    fail "$BASE_PYTHON could not make a virtual environment (see above); on Debian or Ubuntu, install its venv package (python3-venv)"
  fi
fi

# With PIP_CONFIG_FILE set to /dev/null (above), pip reads no configuration
# file, so no extra package index can be mixed in with the ones named here;
# --isolated also keeps it from reading environment settings of its own.
pip_install() {
  "$PYTHON" -I -m pip install --isolated --disable-pip-version-check --no-input --no-cache-dir "$@"
}

# setuptools first: Maia-3 and python-chess are built from source with
# --no-build-isolation, so that the build uses this pinned setuptools rather
# than whatever version pip would otherwise download for the build alone.
say "installing setuptools"
pip_install --constraint "$CONSTRAINTS" setuptools

# PyTorch on its own, without its dependencies, which come from PyPI with
# the rest below. On Linux the package on PyPI pulls in more than a gigabyte
# of NVIDIA GPU libraries, so it comes from PyTorch's CPU-only index
# instead; the macOS package on PyPI already runs on the CPU only. Its
# version is the one in the constraints, which the Linux build's "+cpu"
# label also matches.
say "installing PyTorch for the CPU"
if [ "$OS" = linux ]; then
  pip_install --no-deps --constraint "$CONSTRAINTS" --index-url https://download.pytorch.org/whl/cpu torch
else
  pip_install --no-deps --constraint "$CONSTRAINTS" torch
fi

# Every commit of Maia-3 is version 0.1.0, so pip keeps an installed copy
# from another commit. The commit pip recorded is checked, and a copy from
# another commit is replaced.
MAIA3="maia3 @ git+https://github.com/CSSLab/maia3@$MAIA3_COMMIT"
installed_commit() {
  "$PYTHON" -I -c 'import importlib.metadata as m, json; print(json.loads(m.distribution("maia3").read_text("direct_url.json"))["vcs_info"]["commit_id"])' 2>/dev/null || true
}
say "installing Maia-3 at commit $MAIA3_COMMIT"
pip_install --constraint "$CONSTRAINTS" --no-build-isolation "$MAIA3"
if [ "$(installed_commit)" != "$MAIA3_COMMIT" ]; then
  pip_install --constraint "$CONSTRAINTS" --no-build-isolation --no-deps --force-reinstall "$MAIA3"
fi
[ "$(installed_commit)" = "$MAIA3_COMMIT" ] || fail "Maia-3 is not installed at commit $MAIA3_COMMIT"
"$PYTHON" -I -m pip check --disable-pip-version-check >/dev/null \
  || fail "the installed packages do not agree; see \`$PYTHON -m pip check\`"

# Maia's own download tool, which puts the weights where the engine looks for
# them. A revision that is already in the cache is not downloaded again.
say "fetching the 5M model's weights at revision $WEIGHTS_REVISION"
HF_HOME="$HF_CACHE" HF_HUB_DISABLE_TELEMETRY=1 \
  "$PYTHON" -I -m maia3.cache --model maia3-5m --revision "$WEIGHTS_REVISION" >/dev/null
SNAPSHOT="$HF_CACHE/hub/models--UofTCSSLab--Maia3-5M/snapshots/$WEIGHTS_REVISION"
actual="$("$PYTHON" -I -c 'import hashlib, sys; print(hashlib.sha256(open(sys.argv[1], "rb").read()).hexdigest())' "$SNAPSHOT/maia3-5m.pt")"
if [ "$actual" != "$WEIGHTS_SHA256" ]; then
  # The whole cache, which holds nothing else, is removed: the engine cannot
  # then load a file that failed the check, and the next run downloads it
  # afresh rather than relinking the same stored bytes.
  rm -rf "$HF_CACHE"
  fail "the weights at revision $WEIGHTS_REVISION have SHA-256 $actual, not $WEIGHTS_SHA256, and were removed with $HF_CACHE; if WEIGHTS_REVISION was changed, change WEIGHTS_SHA256 with it, otherwise run this again"
fi

# The handshake is run the way the API runs the engine: the same arguments
# (backend/src/ChessAscent.Api/appsettings.json) and an environment holding
# only the variables the API gives it. On top of Maia's offline settings,
# the network is taken away from the process, so a download attempt would
# fail: by the operating system where it allows that, and otherwise by
# pointing Hugging Face and every proxy setting at a closed local port.
say "checking that the engine answers with no network"
ENGINE_ENV=("HF_HOME=$HF_CACHE" HF_HUB_OFFLINE=1 OMP_NUM_THREADS=1)
NO_NETWORK=()
if [ "$OS" = macos ] && [ -x /usr/bin/sandbox-exec ]; then
  NO_NETWORK=(/usr/bin/sandbox-exec -p '(version 1) (allow default) (deny network*)')
  how="with network access denied by sandbox-exec"
elif [ "$OS" = linux ] && command -v unshare >/dev/null && unshare --user --map-root-user --net true 2>/dev/null; then
  NO_NETWORK=(unshare --user --map-root-user --net)
  how="in a network namespace with no interfaces"
else
  dead=http://127.0.0.1:9
  ENGINE_ENV+=("HF_ENDPOINT=$dead" "HTTP_PROXY=$dead" "HTTPS_PROXY=$dead" "ALL_PROXY=$dead" "http_proxy=$dead" "https_proxy=$dead" "all_proxy=$dead")
  how="with Hugging Face and every proxy pointed at a closed port"
  echo "setup-maia: warning: this system allows no network namespace, so the network was not taken away from the engine; every address it would download from was pointed at a closed port instead" >&2
fi
ENGINE=(
  /usr/bin/env -i "${ENGINE_ENV[@]}"
  "$PYTHON" -I -m maia3 --model maia3-5m --revision "$WEIGHTS_REVISION"
  --local-files-only --device cpu --use-uci-history
)
reply="$(cd "$MAIA" && printf 'uci\nisready\nquit\n' | ${NO_NETWORK[@]+"${NO_NETWORK[@]}"} "${ENGINE[@]}" 2>/dev/null)" \
  || fail "the engine exited with an error; run it by hand to see why: cd $(printf %q "$MAIA") && printf 'uci\\nisready\\n' | $(printf '%q ' "${ENGINE[@]}")"
grep -qx uciok <<<"$reply" && grep -qx readyok <<<"$reply" \
  || fail "the engine did not complete the UCI handshake"
say "the engine answered uciok and readyok, $how"

say "done: $(du -sh "$MAIA" | cut -f1) in $MAIA"
