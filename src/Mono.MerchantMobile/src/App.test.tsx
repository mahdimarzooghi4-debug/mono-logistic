import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'

const branchId = `br_${'a'.repeat(32)}`
const requestId = `drq_${'b'.repeat(32)}`
const baseDelivery = {
  delivery_request_id: requestId, external_reference: 'ORDER-8421', status: 'QUOTED',
  created_at: '2026-10-23T10:00:00Z', mission_id: null, mission_status: null,
  committed_delivery_at: null,
}

function mockConnectedApi(detail: unknown = { ...baseDelivery, quote: null, promise: null }) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/v1/branches') return new Response(JSON.stringify({ branches: [{ branchId, name: 'شعبه ونک', address: 'تهران', status: 'ACTIVE' }] }), { status: 200 })
    if (url === '/v1/delivery-requests?page_size=50') return new Response(JSON.stringify({ items: [baseDelivery], next_cursor: null }), { status: 200 })
    if (url === `/v1/delivery-requests/${requestId}`) return new Response(JSON.stringify(detail), { status: 200 })
    if (url.endsWith('/courier-location')) return new Response(JSON.stringify({ latitude: 35.71, longitude: 51.42, accuracy_meters: 8, observed_at: '2026-10-25T10:00:00Z' }), { status: 200 })
    if (url === '/v1/delivery-requests' && init?.method === 'POST') return new Response(JSON.stringify({ delivery_request_id: requestId }), { status: 200 })
    if (url.endsWith('/confirm') && init?.method === 'POST') return new Response(JSON.stringify({ mission_id: 'mission-real-api-response' }), { status: 200 })
    return new Response(JSON.stringify({ error: { code: 'NOT_FOUND' } }), { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('Mono merchant mobile P0', () => {
  it('does not invent account data before connecting', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: /ارسال‌هایتان را/ })).toBeInTheDocument()
    expect(screen.queryByText('سفارش ۸۴۲۱')).not.toBeInTheDocument()
    expect(screen.getByLabelText('توکن دسترسی')).toBeInTheDocument()
  })

  it('loads home and delivery list from the customer API', async () => {
    const fetchMock = mockConnectedApi()
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('توکن دسترسی'), 'customer-access-token')
    await user.click(screen.getByRole('button', { name: 'اتصال به حساب' }))
    expect(await screen.findByRole('button', { name: /سفارش ORDER-8421/ })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/v1/branches', expect.objectContaining({ cache: 'no-store' }))

    await user.click(screen.getByRole('button', { name: /ارسال‌ها/ }))
    expect(await screen.findByRole('tab', { name: 'فعال' })).toHaveAttribute('aria-selected', 'true')
    await user.type(screen.getByLabelText('جست‌وجوی سفارش'), 'not-there')
    expect(screen.getByText('ارسالی پیدا نشد')).toBeInTheDocument()
  })

  it('creates a delivery using the active branch and an idempotency key', async () => {
    const fetchMock = mockConnectedApi()
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('توکن دسترسی'), 'customer-access-token')
    await user.click(screen.getByRole('button', { name: 'اتصال به حساب' }))
    await screen.findByRole('button', { name: /سفارش ORDER-8421/ })
    await user.click(within(screen.getByRole('navigation')).getByRole('button', { name: '+ثبت' }))
    await user.type(screen.getByLabelText('شماره سفارش'), 'ORDER-MOBILE-5')
    await user.type(screen.getByLabelText('نشانی مبدأ'), 'خیابان ولیعصر، تهران')
    await user.type(screen.getByLabelText('نشانی گیرنده'), 'میدان ونک، تهران')
    await user.click(screen.getByRole('button', { name: /ثبت درخواست و دریافت پیشنهاد/ }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/delivery-requests', expect.objectContaining({ method: 'POST' })))
    const [, request] = fetchMock.mock.calls.find(([url, init]) => String(url) === '/v1/delivery-requests' && init?.method === 'POST')!
    expect(request?.headers).toMatchObject({ 'Idempotency-Key': expect.any(String) })
    const payload = JSON.parse(String(request?.body))
    expect(payload).toMatchObject({ branch_id: branchId, external_reference: 'ORDER-MOBILE-5', requested_service: 'URBAN_DELIVERY' })
    expect(await screen.findByText(/پیشنهاد قیمت و زمان تحویل هنوز/)).toBeInTheDocument()
  })

  it('confirms only a current quote with its matching promise version', async () => {
    const detail = {
      ...baseDelivery,
      quote: { quote_id: 'quote-17', version: 2, expires_at: '2026-10-24T00:00:00Z', is_expired: false, distance_meters: 3200, fare_toman: 85000, buyer_share_toman: 20000, seller_share_toman: 65000 },
      promise: { promise_id: 'promise-17', version: 4, committed_delivery_at: '2026-10-23T14:00:00Z', outcome: 'FEASIBLE' },
    }
    const fetchMock = mockConnectedApi(detail)
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('توکن دسترسی'), 'customer-access-token')
    await user.click(screen.getByRole('button', { name: 'اتصال به حساب' }))
    await user.click(await screen.findByRole('button', { name: /سفارش ORDER-8421/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'تأیید پیشنهاد و درخواست پیک' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/v1/delivery-requests/${requestId}/confirm`, expect.objectContaining({ method: 'POST' })))
    const [, request] = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/confirm'))!
    expect(JSON.parse(String(request?.body))).toEqual({ quote_id: 'quote-17', promise_version: 4 })
    expect(screen.getByText('۸۵٬۰۰۰ تومان')).toBeInTheDocument()
  })

  it('shows fresh courier location only for an assigned mission', async () => {
    const detail = { ...baseDelivery, mission_id: `mis_${'c'.repeat(32)}`, mission_status: 'IN_TRANSIT', quote: null, promise: null }
    const fetchMock = mockConnectedApi(detail)
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('توکن دسترسی'), 'customer-access-token')
    await user.click(screen.getByRole('button', { name: 'اتصال به حساب' }))
    await user.click(await screen.findByRole('button', { name: /سفارش ORDER-8421/ }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('35.71000, 51.42000')).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: 'بازکردن موقعیت در نقشهٔ دستگاه' })).toHaveAttribute('href', 'geo:35.71,51.42?q=35.71,51.42')
    expect(fetchMock).toHaveBeenCalledWith(`/v1/missions/${detail.mission_id}/courier-location`, expect.objectContaining({ cache: 'no-store' }))
  })
})
