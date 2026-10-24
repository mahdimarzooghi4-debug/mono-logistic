#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
MAP_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"
DATA_DIR="$MAP_DIR/data"
PBF_PATH="$DATA_DIR/iran-261023.osm.pbf"
OUTPUT_PATH="$DATA_DIR/iran.pmtiles"
PLANETILER_IMAGE="${PLANETILER_IMAGE:-ghcr.io/onthegomap/planetiler:0.10.2}"
PLANETILER_JVM_HEAP="${PLANETILER_JVM_HEAP:-2g}"

"$SCRIPT_DIR/verify-dataset.sh" "$PBF_PATH"
mkdir -p "$DATA_DIR"

docker run --rm --pull=missing \
  --env "JAVA_TOOL_OPTIONS=-Xmx$PLANETILER_JVM_HEAP" \
  --volume "$DATA_DIR:/data" \
  "$PLANETILER_IMAGE" \
  --osm-path=/data/iran-261023.osm.pbf \
  --output=/data/iran.pmtiles \
  --force

if [[ ! -s "$OUTPUT_PATH" ]]; then
  printf 'Planetiler did not create a non-empty PMTiles archive: %s\n' "$OUTPUT_PATH" >&2
  exit 1
fi

printf 'Created %s\n' "$OUTPUT_PATH"
