#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
MAP_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"
DATA_DIR="$MAP_DIR/data"
EXPECTED_FILE="iran-261023.osm.pbf"
EXPECTED_BYTES="229905951"
CHECKSUM_FILE="$DATA_DIR/$EXPECTED_FILE.sha256"
PBF_PATH="${1:-$DATA_DIR/$EXPECTED_FILE}"

if [[ ! -f "$PBF_PATH" ]]; then
  printf 'OSM dataset is missing: %s\n' "$PBF_PATH" >&2
  printf 'Place iran-261023.osm.pbf in %s/data/ or pass its path as the first argument.\n' "$MAP_DIR" >&2
  exit 2
fi

actual_bytes="$(wc -c < "$PBF_PATH" | tr -d '[:space:]')"
if [[ "$actual_bytes" != "$EXPECTED_BYTES" ]]; then
  printf 'Unexpected PBF size: expected %s bytes, received %s bytes.\n' "$EXPECTED_BYTES" "$actual_bytes" >&2
  exit 1
fi

expected_sha="$(awk '{print $1}' "$CHECKSUM_FILE")"
actual_sha="$(sha256sum "$PBF_PATH" | awk '{print $1}')"
if [[ "$actual_sha" != "$expected_sha" ]]; then
  printf 'PBF SHA-256 mismatch.\nExpected: %s\nActual:   %s\n' "$expected_sha" "$actual_sha" >&2
  exit 1
fi

printf 'Verified %s (%s bytes, SHA-256 %s).\n' "$PBF_PATH" "$actual_bytes" "$actual_sha"
