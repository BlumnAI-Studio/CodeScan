#!/usr/bin/env bash
#
# Rewrites the winget manifest template (0.0.0) into a versioned manifest
# directory ready for submission to microsoft/winget-pkgs, or for local
# install via `winget install --manifest <path>`.
#
# Usage:
#   ./update-manifests.sh <version> <checksums.txt-path> [release-date]
#
# Example:
#   ./update-manifests.sh 0.4.1 ./dist/checksums.txt 2026-05-17

set -eu

VERSION="${1:?version required (e.g. 0.4.1)}"
SUMS_FILE="${2:?path to checksums.txt required}"
RELEASE_DATE="${3:-$(date -u +%Y-%m-%d)}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TEMPLATE_DIR="${SCRIPT_DIR}/manifests/p/psmon/CodeScan/0.0.0"
OUT_DIR="${SCRIPT_DIR}/manifests/p/psmon/CodeScan/${VERSION}"

if [ ! -d "$TEMPLATE_DIR" ]; then
    echo "Template directory not found: $TEMPLATE_DIR" >&2
    exit 1
fi

SHA_WIN_X64="$(grep -F "codescan-win-x64.zip" "$SUMS_FILE" | awk '{print $1}' | head -n1)"
if [ -z "$SHA_WIN_X64" ]; then
    echo "Missing SHA256 for codescan-win-x64.zip in $SUMS_FILE" >&2
    exit 1
fi

mkdir -p "$OUT_DIR"

# winget manifests use SHA256 in uppercase hex (per schema)
SHA_UPPER="$(printf '%s' "$SHA_WIN_X64" | tr '[:lower:]' '[:upper:]')"

for src in "$TEMPLATE_DIR"/*.yaml; do
    fname="$(basename "$src")"
    # 1) drop the template-only banner comment (lines 2-6 of the installer template)
    # 2) substitute the placeholders
    sed \
        -e '/^# Template manifest/,/^$/d' \
        -e "s|PackageVersion: 0\.0\.0|PackageVersion: ${VERSION}|g" \
        -e "s|{VERSION}|${VERSION}|g" \
        -e "s|{SHA256}|${SHA_UPPER}|g" \
        -e "s|ReleaseDate: 1970-01-01|ReleaseDate: ${RELEASE_DATE}|g" \
        "$src" > "${OUT_DIR}/${fname}"
done

echo "Generated:"
ls -1 "$OUT_DIR"
