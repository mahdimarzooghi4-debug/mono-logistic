#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage:
  PLANETILER_IMAGE=<versioned-image-or-digest> bash ops/maps/build-iran-tiles.sh <iran.osm.pbf> <release-id>

Environment:
  MAP_DATA_DIR       Data root (default: /srv/mono/maps)
  PLANETILER_IMAGE   Required Planetiler container image; do not use :latest
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  exit 1
}

[[ $# -eq 2 ]] || { usage; exit 2; }
pbf_path=$1
release_id=$2
map_data_dir=${MAP_DATA_DIR:-/srv/mono/maps}
planetiler_image=${PLANETILER_IMAGE:-}

[[ -n "$planetiler_image" ]] || fail "Set PLANETILER_IMAGE to a reviewed, version-pinned image."
[[ "$planetiler_image" != *:latest ]] || fail "Do not use a mutable :latest Planetiler image."
[[ "$release_id" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$ ]] || fail "Invalid release id."
[[ -f "$pbf_path" && -r "$pbf_path" ]] || fail "PBF file does not exist or is not readable: $pbf_path"
command -v docker >/dev/null || fail "docker is required."
command -v sqlite3 >/dev/null || fail "sqlite3 is required for archive validation."
command -v sha256sum >/dev/null || fail "sha256sum is required."

pbf_path=$(cd "$(dirname "$pbf_path")" && printf '%s/%s' "$PWD" "$(basename "$pbf_path")")
mkdir -p "$map_data_dir/staging" "$map_data_dir/releases"
map_data_dir=$(cd "$map_data_dir" && pwd)
stage_dir="$map_data_dir/staging/iran-$release_id"
archive_name="iran-$release_id.mbtiles"
stage_archive="$stage_dir/$archive_name"
release_dir="$map_data_dir/releases/iran-$release_id"
release_archive="$release_dir/$archive_name"

[[ ! -e "$stage_dir" ]] || fail "Staging directory already exists: $stage_dir"
[[ ! -e "$release_dir" ]] || fail "Release already exists: $release_dir"

mkdir -m 0750 "$stage_dir"
cleanup() {
  status=$?
  if [[ $status -ne 0 ]]; then
    printf 'Build failed. Staging files were kept for diagnosis: %s\n' "$stage_dir" >&2
  fi
}
trap cleanup EXIT

docker run --rm \
  --volume "$pbf_path:/input/iran.osm.pbf:ro" \
  --volume "$stage_dir:/output:rw" \
  "$planetiler_image" \
  --osm-path=/input/iran.osm.pbf \
  --output="/output/$archive_name"

[[ -s "$stage_archive" ]] || fail "Planetiler did not create a non-empty MBTiles archive."

integrity=$(sqlite3 "$stage_archive" 'PRAGMA integrity_check;')
[[ "$integrity" == "ok" ]] || fail "MBTiles SQLite integrity check failed: $integrity"

table_count=$(sqlite3 "$stage_archive" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='tiles';")
[[ "$table_count" == "1" ]] || fail "MBTiles tiles table is missing."

tile_count=$(sqlite3 "$stage_archive" 'SELECT COUNT(*) FROM tiles;')
[[ "$tile_count" =~ ^[0-9]+$ && "$tile_count" -gt 0 ]] || fail "MBTiles archive contains no tiles."

pbf_sha=$(sha256sum "$pbf_path" | awk '{print $1}')
archive_sha=$(sha256sum "$stage_archive" | awk '{print $1}')
archive_bytes=$(wc -c < "$stage_archive" | tr -d ' ')
built_at=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
{
  printf 'release_id\t%s\n' "$release_id"
  printf 'source_file\t%s\n' "$(basename "$pbf_path")"
  printf 'source_sha256\t%s\n' "$pbf_sha"
  printf 'planetiler_image\t%s\n' "$planetiler_image"
  printf 'built_at_utc\t%s\n' "$built_at"
  printf 'archive_file\t%s\n' "$archive_name"
  printf 'archive_bytes\t%s\n' "$archive_bytes"
  printf 'archive_sha256\t%s\n' "$archive_sha"
  printf 'tile_count\t%s\n' "$tile_count"
} > "$stage_dir/build-manifest.tsv"

# Publish the archive and manifest together by renaming one directory on the same filesystem.
mv "$stage_dir" "$release_dir"

printf 'Validated map archive: %s\n' "$release_archive"
printf 'Tiles: %s | bytes: %s | sha256: %s\n' "$tile_count" "$archive_bytes" "$archive_sha"
printf 'This build does not activate or replace the map currently served.\n'
