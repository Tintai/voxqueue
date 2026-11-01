#!/bin/bash
# Build the Linux binary inside Ubuntu 22.04.
#
# Why: PyInstaller cannot cross-compile, so the binary inherits the glibc of the
# machine that builds it. Built on 24.04 it required GLIBC_2.38 (from
# libpython3.12), and Ubuntu 22.04 ships 2.35 — the process died before drawing a
# window, with no message on screen. Building on the OLDEST target system is the
# only way to get a binary that runs on it and everything newer.
#
# Ubuntu 22.04 has Python 3.10, which is the minimum this code needs (it uses
# PEP 604 unions such as `str | None`).
#
# Usage:  bash build/build_linux_jammy.sh [--shell]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="ubuntu:22.04"

if ! command -v docker >/dev/null 2>&1; then
    echo "ERROR: docker is required for this script" >&2
    exit 1
fi

if [ "${1:-}" = "--shell" ]; then
    exec docker run --rm -it -v "$ROOT:/src" -w /src "$IMAGE" bash
fi

echo "== building in $IMAGE =="

docker run --rm -v "$ROOT:/src" -w /src "$IMAGE" bash -euo pipefail -c '
    export DEBIAN_FRONTEND=noninteractive

    echo "-- apt --"
    apt-get update -qq
    apt-get install -y -qq --no-install-recommends \
        python3 python3-pip python3-venv \
        libpython3.10 \
        binutils file dpkg-dev \
        libgl1 libegl1 libglib2.0-0 libfontconfig1 libfreetype6 \
        libdbus-1-3 libx11-6 libxkbcommon0 libpulse0 libbrotli1 \
        libxcb1 libxcb-shape0 libxcb-render0 libxcb-randr0 libxcb-shm0 libxcb-sync1 libxcb-xfixes0 libxcb-cursor0 libxcb-icccm4 libxcb-image0 \
        libxcb-keysyms1 libxcb-render-util0 libxkbcommon-x11-0 >/dev/null

    echo "-- venv --"
    python3 -m venv /venv
    /venv/bin/pip install -q --upgrade pip
    /venv/bin/pip install -q -r build/requirements.txt

    echo "-- versions --"
    /venv/bin/python --version
    /venv/bin/pip show PySide6 pyinstaller 2>/dev/null | grep -E "^(Name|Version)"

    echo "-- build --"
    PYTHON=/venv/bin/python bash build/build_linux.sh
'
