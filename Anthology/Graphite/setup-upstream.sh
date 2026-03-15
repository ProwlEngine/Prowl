#!/usr/bin/env bash
# Clones the upstream Veldrid repositories into the upstream/ directory.
# These are used as read-only references during porting and are gitignored.
#
# Usage: ./setup-upstream.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UPSTREAM_DIR="$SCRIPT_DIR/upstream"

if [ -d "$UPSTREAM_DIR" ]; then
    echo "upstream/ directory already exists. Remove it first if you want a fresh clone."
    echo "  rm -rf upstream/"
    exit 1
fi

mkdir -p "$UPSTREAM_DIR"

echo "Cloning veldrid..."
git clone git@github.com:veldrid/veldrid.git "$UPSTREAM_DIR/veldrid"

echo "Cloning veldrid-samples..."
git clone git@github.com:mellinoe/veldrid-samples.git "$UPSTREAM_DIR/veldrid-samples"

echo "Cloning veldrid-spirv (with submodules)..."
git clone --recurse-submodules git@github.com:veldrid/veldrid-spirv.git "$UPSTREAM_DIR/veldrid-spirv"

echo ""
echo "Done. Upstream repos cloned into upstream/"
echo "These are gitignored and used as read-only references during porting."
