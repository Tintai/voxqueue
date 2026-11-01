#!/bin/sh
# Start VoxQueue from the folder this script sits in, so it works when the
# archive is unpacked anywhere and the desktop runs it from a different directory.
cd "$(dirname "$0")" || exit 1
exec ./VoxQueue "$@"
