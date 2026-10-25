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
function connectedApi(routeContext: unknown = route) {
  let responded = false
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/courier/v1/session') return new Response(JSON.stringify({ courier_id: 'cur_123', identity_domain: 'mono' }), { status: 200 })
    if (url === '/courier/v1/mission-offers') return new Response(JSON.stringify(responded ? [] : [offer]), { status: 200 })
    if (url.endsWith('/route-context')) return new Response(JSON.stringify(routeContext), { status: 200 })
    if (/\/(accept|decline)$/.test(url) && init?.method === 'POST') { responded = true; return new Response(JSON.stringify({ offer: { ...offer, status: url.endsWith('/accept') ? 'ACCEPTED' : 'DECLINED' } }), { status: 200 }) }
    return new Response(JSON.stringify({ error: { code: 'NOT_FOUND' } }), { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}
async function connect(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('توکن دسترسی'), 'dev-courier-token')
  await user.click(screen.getByRole('button', { name: 'اتصال' }))
}

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.unstubAllEnvs() })

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
    expect(await screen.findByText('فعلاً مأموریتی برای شما نیست')).toBeInTheDocument()
  })

  it('declines the offer through its API action', async () => {
    const fetchMock = connectedApi()
    const user = userEvent.setup(); render(<App />); await connect(user)
    await user.click(await screen.findByRole('button', { name: 'رد پیشنهاد' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/courier/v1/mission-offers/${offer.offer_id}/decline`, expect.objectContaining({ method: 'POST' })))
  })
})
