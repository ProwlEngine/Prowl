#!/usr/bin/env bash

set -euo pipefail

usage() {
    cat <<EOF
Usage:
  $(basename "$0") -n <Name> -s <Source> [-b <Branch>]

Options:
  -n, --name      Subfolder name inside the monorepo (e.g. Vector)
  -s, --source    Path or URL of the source repository
  -b, --branch    Branch to import (defaults to source repo's current branch)

Example:
  ./scripts/merge-library.sh -n Vector -s ../Prowl.Vector
EOF
    exit 1
}

NAME=""
SOURCE=""
BRANCH=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        -n|--name)
            NAME="$2"
            shift 2
            ;;
        -s|--source)
            SOURCE="$2"
            shift 2
            ;;
        -b|--branch)
            BRANCH="$2"
            shift 2
            ;;
        -h|--help)
            usage
            ;;
        *)
            echo "Unknown argument: $1"
            usage
            ;;
    esac
done

[[ -z "$NAME" || -z "$SOURCE" ]] && usage

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
FILTER_REPO="$SCRIPT_DIR/git-filter-repo"
SOLUTION="$REPO_ROOT/Anthology.slnx"

# --- sanity checks ---
[[ -d "$REPO_ROOT/.git" ]] || {
    echo "Not inside the Anthology repo: $REPO_ROOT"
    exit 1
}

[[ ! -e "$REPO_ROOT/$NAME" ]] || {
    echo "Folder '$NAME' already exists in the monorepo."
    exit 1
}

[[ -f "$FILTER_REPO" ]] || {
    echo "Missing scripts/git-filter-repo"
    exit 1
}

SOURCE="$(realpath "$SOURCE")"

[[ -d "$SOURCE/.git" ]] || {
    echo "Source is not a git repo: $SOURCE"
    exit 1
}

if [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]]; then
    echo "Anthology working tree is dirty. Commit or stash first."
    exit 1
fi

# --- fresh, detached clone ---
TMP="$(mktemp -d "/tmp/anthology_${NAME}_XXXXXX")"

echo "Cloning $SOURCE -> $TMP"
git clone --no-hardlinks --quiet "$SOURCE" "$TMP"

if [[ -z "$BRANCH" ]]; then
    BRANCH="$(git -C "$TMP" rev-parse --abbrev-ref HEAD)"
fi

echo "Importing branch '$BRANCH' into '$NAME/'"

# --- rewrite history ---
pushd "$TMP" >/dev/null
python "$FILTER_REPO" --force --to-subdirectory-filter "$NAME"
popd >/dev/null

# --- merge into monorepo ---
REMOTE="import_$NAME"

cleanup() {
    git -C "$REPO_ROOT" remote remove "$REMOTE" >/dev/null 2>&1 || true
    rm -rf "$TMP"
}
trap cleanup EXIT

git -C "$REPO_ROOT" remote add "$REMOTE" "$TMP"
git -C "$REPO_ROOT" fetch --quiet "$REMOTE"

git -C "$REPO_ROOT" merge \
    --allow-unrelated-histories \
    --no-edit \
    -m "Fold in $NAME with full history" \
    "$REMOTE/$BRANCH"

echo
echo "Done. '$NAME/' folded in (merge committed)."
echo "Verify history:"
echo "  git log --oneline -- $NAME/"