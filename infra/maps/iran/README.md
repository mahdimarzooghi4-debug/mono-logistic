# Iran OSM data and private map runtime

This package prepares the whole-country OSM snapshot for Mono's self-hosted routing and map display. The source PBF and generated archives stay outside Git; only the source record, checks, scripts, and service configuration are versioned here.

## Pinned source snapshot

- Provider: [Geofabrik Iran](https://download.geofabrik.de/asia/iran.html)
- Download: [iran-latest.osm.pbf](https://download.geofabrik.de/asia/iran-latest.osm.pbf)
- Snapshot file: `iran-261023.osm.pbf`
- OSM data timestamp: `2026-10-23T20:20:51Z`
- Size: `229905951` bytes
- SHA-256: `a1409610c0df9d3c930b93b1301084b25367d6815936d5646e1769b556704011`
- Source data license: OpenStreetMap ODbL; display `© OpenStreetMap contributors` on the map and review ODbL obligations for distributed or derived data.

To reproduce the pinned build, put the downloaded file at:

```text
infra/maps/iran/data/iran-261023.osm.pbf
```

The local data directory is ignored by Git. The verification script rejects a different file until its source record and checksum are deliberately updated together:

```bash
bash infra/maps/iran/scripts/verify-dataset.sh
```

## Build the visual map tiles

Planetiler builds an OSM-derived PMTiles archive from the local PBF. Its pinned image tag is `0.10.2`.

```bash
bash infra/maps/iran/scripts/build-map-tiles.sh
```

The first Planetiler run may fetch its supporting basemap datasets. Allow at least 1 GB of free SSD space plus roughly 5–10 times the PBF size for build workspace and output, with additional capacity for Valhalla's routing graph. Planetiler's documented starting point for JVM heap is about half the input PBF size; the script defaults to a 2 GB heap to leave room for memory-mapped data.

## Start local/private services

After the PBF is verified and `data/iran.pmtiles` exists:

```bash
cd infra/maps/iran
docker compose up -d
```

- Valhalla serves its routing API on container port `8002`, with no host port published. Attach the Mono API container to Docker network `mono-maps-private`; set `MonoMaps__Provider=Valhalla` and `MonoMaps__Valhalla__BaseUrl=http://valhalla:8002/`.
- TileServer GL serves PMTiles locally on `127.0.0.1:8090`, suitable for a host reverse proxy. Do not expose the Valhalla port publicly.
- The browser map still needs a MapLibre style configured against the tile service and visible OSM attribution. The TileServer GL local preview is available at `http://127.0.0.1:8090`.

This compose file is a self-hosting foundation, not a Stage or Production deployment. No server is provisioned by this package. Before Stage, pin and review container digests, provide TLS/reverse proxy and resource limits, wire the Maps Gateway to the private Valhalla URL, verify supported Car/Bike/Walk profiles over real routes, and test the tile style on web and mobile.

## Data handling

- Do not commit the PBF, generated PMTiles, or Valhalla graph artifacts.
- Refresh only by choosing a new dated OSM snapshot, recording its data timestamp, byte size and SHA-256, then updating the manifest/check script in review.
- Keep live courier/customer coordinates inside Mono-controlled services. Do not send them to public tile or routing services; Valhalla requests use JSON POST bodies rather than URL query strings.
