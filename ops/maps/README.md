# Mono self-hosted map tiles

This directory prepares an OSM-derived Iran vector-tile archive. It does not provision a server or change the currently served map.

## Build an archive

Requirements: Docker, `sqlite3`, `sha256sum`, enough temporary disk/RAM for Planetiler, and the Iran `.osm.pbf` extract.

Choose and review a version-pinned Planetiler image first. Prefer an immutable image digest for repeatable builds; never use `:latest`.

```bash
PLANETILER_IMAGE='ghcr.io/onthegomap/planetiler:<reviewed-version>' \
MAP_DATA_DIR='/srv/mono/maps' \
bash ops/maps/build-iran-tiles.sh /path/to/iran.osm.pbf 2026-10-25
```

The script writes a new release directory under `releases/iran-<release-id>/`, validates SQLite integrity and the MBTiles tile table, and records source/archive checksums and build metadata. It publishes the archive and manifest together only after validation, never overwrites an existing release, and does not switch the active service. Failed staging files are retained for diagnosis. Planetiler performs full snapshot builds; refresh the archive from a new dated PBF rather than expecting live diffs.

Planetiler accepts OSM PBF and writes MBTiles. Its upstream guide notes that processing needs temporary disk and RAM that scale with input size; measure the actual Iran extract before selecting server resources: [Planetiler README](https://github.com/onthegomap/planetiler#run).

## Serve a validated archive

After installing Docker on the map host, select a reviewed, version-pinned TileServer GL image and an archive filename produced above. Bind the container only to localhost so a separately configured HTTPS reverse proxy can publish the map endpoint:

```bash
docker run --detach --name mono-tiles --restart unless-stopped \
  --volume /srv/mono/maps/releases:/data:ro \
  --publish 127.0.0.1:8080:8080 \
  'maptiler/tileserver-gl:<reviewed-version>' \
  --file iran-2026-10-25/iran-2026-10-25.mbtiles
```

Replace the sample image version and archive filename with the reviewed values. TileServer GL documents Docker use with an MBTiles file: [TileServer GL README](https://github.com/maptiler/tileserver-gl#getting-started-with-docker).

The example only starts the local tile process. Before Stage, configure the HTTPS hostname/reverse proxy, monitoring, restart/rollback procedure, and access policy on the actual host. Do not expose port 8080 directly to the public internet.

## Still required before Stage

- Open the archive in the Mono client with the approved MapLibre style and check Tehran labels, roads, zoom levels, and pickup/drop-off markers.
- Confirm visible `© OpenStreetMap contributors` attribution and review ODbL obligations.
- Load-test representative tile requests and size the host and bandwidth from measured results.
- Configure the client to use the owner-operated tile/style URL and verify no public tile-provider fallback exists.
- Document update cadence, archive activation/rollback, backup, monitoring, and data refresh.
- Keep this service separate from Valhalla routing, geocoding, and device navigation.
