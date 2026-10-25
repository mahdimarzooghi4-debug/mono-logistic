import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'

vi.mock('maplibre-gl', () => ({
  Map: class { on() { return this } fitBounds() {} remove() {} },
  Marker: class { setLngLat() { return this } addTo() { return this } remove() {} },
  setWorkerUrl: vi.fn(),
}))

const offer = { offer_id: 'ofr_123', mission_id: 'mis_123', courier_id: 'cur_123', zone_id: 'zone_1', status: 'OFFERED', created_at: '2026-10-25T10:00:00Z', updated_at: '2026-10-25T10:00:00Z', expires_at: '2026-10-25T10:05:00Z' }
const route = { offer_id: offer.offer_id, mission_id: offer.mission_id, expires_at: offer.expires_at, coordinates_status: 'COMPLETE', pickup: { location_id: 'loc_pickup', coordinate: { latitude: 35.7, longitude: 51.4 } }, dropoff: { location_id: 'loc_dropoff', coordinate: { latitude: 35.71, longitude: 51.42 } } }
const originalGeolocation = Object.getOwnPropertyDescriptor(navigator, 'geolocation')
function connectedApi(routeContext: unknown = route) {
  let responded = false
  let activeMission: Record<string, unknown> | null = null
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/courier/v1/session') return new Response(JSON.stringify({ courier_id: 'cur_123', identity_domain: 'mono' }), { status: 200 })
    if (url === '/courier/v1/mission-offers') return new Response(JSON.stringify(responded ? [] : [offer]), { status: 200 })
    if (url === '/courier/v1/missions/active') return new Response(JSON.stringify({ mission: activeMission }), { status: 200 })
    if (url === '/courier/v1/location' && init?.method === 'POST') return new Response(JSON.stringify({ applied: true }), { status: 200 })
    if (url === '/courier/v1/location' && init?.method === 'DELETE') return new Response(null, { status: 204 })
    if (url.endsWith('/route-context')) return new Response(JSON.stringify(routeContext), { status: 200 })
    if (/\/(accept|decline)$/.test(url) && init?.method === 'POST') { responded = true; if (url.endsWith('/accept')) activeMission = { ...route, status: 'COURIER_ASSIGNED' }; return new Response(JSON.stringify({ offer: { ...offer, status: url.endsWith('/accept') ? 'ACCEPTED' : 'DECLINED' } }), { status: 200 }) }
    if (url.endsWith('/events') && init?.method === 'POST' && activeMission) {
      const body = JSON.parse(String(init.body)) as { event_type: string }
      const statusByEvent: Record<string, string> = { 'mission.arrived_pickup': 'ARRIVED_PICKUP', 'mission.handed_to_courier': 'HANDED_TO_COURIER', 'mission.picked_up': 'PICKED_UP', 'mission.in_transit': 'IN_TRANSIT', 'mission.arrived_dropoff': 'ARRIVED_DROPOFF', 'mission.delivered': 'DELIVERED' }
      activeMission = { ...activeMission, status: statusByEvent[body.event_type] }
      return new Response(JSON.stringify({ status: statusByEvent[body.event_type] }), { status: 200 })
    }
    return new Response(JSON.stringify({ error: { code: 'NOT_FOUND' } }), { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}
async function connect(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('توکن دسترسی'), 'dev-courier-token')
  await user.click(screen.getByRole('button', { name: 'اتصال' }))
}

afterEach(() => {
  cleanup(); vi.unstubAllGlobals(); vi.unstubAllEnvs(); vi.restoreAllMocks()
  if (originalGeolocation) Object.defineProperty(navigator, 'geolocation', originalGeolocation)
  else Reflect.deleteProperty(navigator, 'geolocation')
})

describe('Courier offer mobile', () => {
  it('does not show invented offer details before connection', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: 'آمادهٔ دریافت مأموریت' })).toBeInTheDocument()
    expect(screen.queryByText('mis_123')).not.toBeInTheDocument()
  })

  it('loads offer and private route context from the API', async () => {
    vi.stubEnv('VITE_MAP_STYLE_URL', 'https://maps.example.test/style.json')
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    expect(await screen.findByRole('heading', { name: 'پیشنهاد جدید' })).toBeInTheDocument()
    expect(screen.getByText('mis_123')).toBeInTheDocument()
    expect(screen.getByText('نقطهٔ دریافت مرسوله')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(`/courier/v1/mission-offers/${offer.offer_id}/route-context`, expect.objectContaining({ cache: 'no-store' }))
  })

  it('never renders markers or route stops while coordinates are pending', async () => {
    connectedApi({ ...route, coordinates_status: 'COORDINATES_PENDING', pickup: { ...route.pickup, coordinate: null } })
    const user = userEvent.setup(); render(<App />); await connect(user)
    expect(await screen.findByText(/مختصات مبدأ یا مقصد هنوز آماده نیست/)).toBeInTheDocument()
    expect(screen.queryByText('نقطهٔ دریافت مرسوله')).not.toBeInTheDocument()
  })

  it('accepts an offer with an idempotency key and refreshes offers', async () => {
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await user.click(await screen.findByRole('button', { name: 'قبول مأموریت' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/courier/v1/mission-offers/${offer.offer_id}/accept`, expect.objectContaining({ method: 'POST' })))
    const [, request] = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/accept'))!
    expect(request?.headers).toMatchObject({ 'Idempotency-Key': expect.any(String) })
    expect(await screen.findByRole('heading', { name: 'مأموریت در حال اجرا' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'رسیدم به مبدأ' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'مسیریابی تا فروشگاه' })).toHaveAttribute('href', 'geo:35.7,51.4?q=35.7,51.4')
  })

  it('can create lifecycle idempotency keys when a LAN browser has no secure-context UUID API', async () => {
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await user.click(await screen.findByRole('button', { name: 'قبول مأموریت' }))
    vi.stubGlobal('crypto', {})
    await user.click(await screen.findByRole('button', { name: 'رسیدم به مبدأ' }))
    const [, request] = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/events'))!
    expect(request?.headers).toMatchObject({ 'Idempotency-Key': expect.stringMatching(/^mono-/) })
  })

  it('shares courier GPS only after consent, throttles it to the active mission, and stops the browser watch', async () => {
    const onPosition: PositionCallback[] = []
    const watchPosition = vi.fn((success: PositionCallback) => { onPosition.push(success); return 17 })
    const clearWatch = vi.fn()
    Object.defineProperty(navigator, 'geolocation', { configurable: true, value: { watchPosition, clearWatch } })
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await user.click(await screen.findByRole('button', { name: 'قبول مأموریت' }))
    await screen.findByRole('button', { name: 'اشتراک موقعیت زنده' })
    const deleteCalls = () => fetchMock.mock.calls.filter(([url, init]) => String(url) === '/courier/v1/location' && init?.method === 'DELETE').length
    await waitFor(() => expect(deleteCalls()).toBe(1))
    expect(watchPosition).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: 'اشتراک موقعیت زنده' }))
    await waitFor(() => expect(watchPosition).toHaveBeenCalledWith(expect.any(Function), expect.any(Function), expect.objectContaining({ enableHighAccuracy: true, maximumAge: 5_000 })))
    onPosition[0]({
      coords: { latitude: 35.72, longitude: 51.43, accuracy: 12, altitude: null, altitudeAccuracy: null, heading: 180, speed: 4 },
      timestamp: Date.now(),
    } as GeolocationPosition)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/courier/v1/location', expect.objectContaining({ method: 'POST' })))
    const [, request] = fetchMock.mock.calls.find(([url, init]) => String(url) === '/courier/v1/location' && init?.method === 'POST')!
    expect(JSON.parse(String(request?.body))).toMatchObject({ latitude: 35.72, longitude: 51.43, accuracy_meters: 12, heading_degrees: 180, speed_meters_per_second: 4, active_route_id: null })
    expect(request?.headers).toMatchObject({ Authorization: 'Bearer dev-courier-token' })

    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
    document.dispatchEvent(new Event('visibilitychange'))
    await waitFor(() => expect(clearWatch).toHaveBeenCalledWith(17))
    const deletesBeforeBackground = deleteCalls()
    expect(deletesBeforeBackground).toBe(1)
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
    document.dispatchEvent(new Event('visibilitychange'))
    await waitFor(() => expect(watchPosition).toHaveBeenCalledTimes(2))

    await user.click(await screen.findByRole('button', { name: 'توقف اشتراک موقعیت' }))
    await waitFor(() => expect(clearWatch).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(deleteCalls()).toBe(deletesBeforeBackground + 1))
    expect(fetchMock).toHaveBeenCalledWith('/courier/v1/location', expect.objectContaining({ method: 'DELETE', keepalive: true }))
  })


  it('reports an accepted mission milestone and refreshes its current status', async () => {
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await user.click(await screen.findByRole('button', { name: 'قبول مأموریت' }))
    await user.click(await screen.findByRole('button', { name: 'رسیدم به مبدأ' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/courier/v1/missions/${offer.mission_id}/events`, expect.objectContaining({ method: 'POST' })))
    const [, request] = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/events'))!
    expect(JSON.parse(String(request?.body))).toMatchObject({ event_type: 'mission.arrived_pickup', reason: 'COURIER_MOBILE_CONFIRMED' })
    expect(request?.headers).toMatchObject({ 'Idempotency-Key': expect.any(String) })
    expect(await screen.findByRole('button', { name: 'تحویل مرسوله را تأیید می‌کنم' })).toBeInTheDocument()
  })

  it('routes to the shop before pickup, to the customer after pickup, and completes the whole mission lifecycle', async () => {
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await user.click(await screen.findByRole('button', { name: 'قبول مأموریت' }))

    const steps = [
      { button: 'رسیدم به مبدأ', event: 'mission.arrived_pickup', status: 'ARRIVED_PICKUP', route: 'geo:35.7,51.4?q=35.7,51.4', label: 'مسیریابی تا فروشگاه' },
      { button: 'تحویل مرسوله را تأیید می‌کنم', event: 'mission.handed_to_courier', status: 'HANDED_TO_COURIER', route: 'geo:35.7,51.4?q=35.7,51.4', label: 'مسیریابی تا فروشگاه' },
      { button: 'مرسوله را تحویل گرفتم', event: 'mission.picked_up', status: 'PICKED_UP', route: 'geo:35.71,51.42?q=35.71,51.42', label: 'مسیریابی تا مشتری' },
      { button: 'حرکت به سمت مقصد', event: 'mission.in_transit', status: 'IN_TRANSIT', route: 'geo:35.71,51.42?q=35.71,51.42', label: 'مسیریابی تا مشتری' },
      { button: 'رسیدم به مقصد', event: 'mission.arrived_dropoff', status: 'ARRIVED_DROPOFF', route: 'geo:35.71,51.42?q=35.71,51.42', label: 'مسیریابی تا مشتری' },
      { button: 'تحویل به گیرنده انجام شد', event: 'mission.delivered', status: 'DELIVERED', route: null, label: null },
    ]
    for (const step of steps) {
      await user.click(await screen.findByRole('button', { name: step.button }))
      await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/events'))).toHaveLength(steps.indexOf(step) + 1))
      const [, request] = fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/events')).at(-1)!
      expect(JSON.parse(String(request?.body))).toMatchObject({ event_type: step.event, reason: 'COURIER_MOBILE_CONFIRMED' })
      if (step.label) {
        expect(await screen.findByRole('link', { name: step.label })).toHaveAttribute('href', step.route)
      } else {
        expect(await screen.findByText('این مأموریت پایان یافته است.')).toBeInTheDocument()
        expect(screen.queryByRole('link', { name: /مسیریابی/ })).not.toBeInTheDocument()
      }
    }
  })


  it('refreshes offers when the courier returns to the visible app', async () => {
    const intervalSpy = vi.spyOn(window, 'setInterval')
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await screen.findByRole('heading', { name: 'پیشنهاد جدید' })
    expect(intervalSpy).toHaveBeenCalledWith(expect.any(Function), 15_000)
    const offersCalls = () => fetchMock.mock.calls.filter(([url]) => String(url) === '/courier/v1/mission-offers').length
    const before = offersCalls()
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
    document.dispatchEvent(new Event('visibilitychange'))
    expect(offersCalls()).toBe(before)
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
    document.dispatchEvent(new Event('visibilitychange'))
    await waitFor(() => expect(offersCalls()).toBe(before + 1))
  })

  it('declines the offer through its API action', async () => {
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await user.click(await screen.findByRole('button', { name: 'رد پیشنهاد' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/courier/v1/mission-offers/${offer.offer_id}/decline`, expect.objectContaining({ method: 'POST' })))
  })

  it('clears an expired in-memory token and stops polling until the courier reconnects', async () => {
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await screen.findByRole('heading', { name: 'پیشنهاد جدید' })
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      if (String(input) === '/courier/v1/session') return new Response(JSON.stringify({ error: { code: 'TOKEN_EXPIRED' } }), { status: 401 })
      return new Response(JSON.stringify({ mission: null }), { status: 200 })
    })

    await user.click(screen.getByRole('button', { name: 'به‌روزرسانی پیشنهادها' }))
    expect(await screen.findByRole('heading', { name: 'آمادهٔ دریافت مأموریت' })).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('نشست شما معتبر نیست یا منقضی شده است')
    expect(screen.getByLabelText('توکن دسترسی')).toHaveValue('')
    const sessionCalls = fetchMock.mock.calls.filter(([url]) => String(url) === '/courier/v1/session').length
    await new Promise(resolve => setTimeout(resolve, 30))
    expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/courier/v1/session')).toHaveLength(sessionCalls)
  })
})
