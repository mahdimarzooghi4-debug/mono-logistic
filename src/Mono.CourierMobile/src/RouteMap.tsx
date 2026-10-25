import { useEffect, useRef } from 'react'
import { Map as MapLibreMap, Marker, setWorkerUrl } from 'maplibre-gl'
import mapWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'
import 'maplibre-gl/dist/maplibre-gl.css'

setWorkerUrl(mapWorkerUrl)
type Coordinate = { latitude: number; longitude: number }
type RoutePoint = { location_id: string; coordinate: Coordinate | null }
type RouteContext = { coordinates_status: string; pickup: RoutePoint; dropoff: RoutePoint }
function valid(value: Coordinate | null): value is Coordinate {
  return !!value && Number.isFinite(value.latitude) && value.latitude >= -90 && value.latitude <= 90 && Number.isFinite(value.longitude) && value.longitude >= -180 && value.longitude <= 180
}

export default function RouteMap({ route, styleUrl, onError }: { route: RouteContext; styleUrl: string; onError: (message: string) => void }) {
  const container = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const element = container.current
    const pickup = route.pickup.coordinate
    const dropoff = route.dropoff.coordinate
    if (!element || !styleUrl || route.coordinates_status !== 'COMPLETE' || !valid(pickup) || !valid(dropoff)) return
    let map: MapLibreMap | null = null
    let markers: Marker[] = []
    try {
      map = new MapLibreMap({ container: element, style: styleUrl, center: [pickup.longitude, pickup.latitude], zoom: 12 })
      markers = [
        new Marker({ color: '#f1973b' }).setLngLat([pickup.longitude, pickup.latitude]).addTo(map),
        new Marker({ color: '#285c50' }).setLngLat([dropoff.longitude, dropoff.latitude]).addTo(map),
      ]
      map.on('error', () => onError('map-load-error'))
      if (pickup.latitude !== dropoff.latitude || pickup.longitude !== dropoff.longitude) map.fitBounds([[pickup.longitude, pickup.latitude], [dropoff.longitude, dropoff.latitude]], { padding: 64, maxZoom: 15, duration: 0 })
    } catch { onError('map-load-error') }
    return () => { markers.forEach(marker => marker.remove()); map?.remove() }
  }, [route, styleUrl, onError])
  return <div className="map-canvas" ref={container} aria-label="نقشهٔ مبدأ و مقصد" role="img"><span className="map-placeholder">نمای نقشهٔ مبدأ و مقصد</span></div>
}
