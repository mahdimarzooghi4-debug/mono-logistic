import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('merchant portal delivery flow', () => {
  it('connects with a customer token and creates a scoped, idempotent delivery request', async () => {
    const branchId = `br_${'a'.repeat(32)}`
    const requestId = `drq_${'b'.repeat(32)}`
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url === '/v1/branches') {
        return new Response(JSON.stringify({ branches: [{ branchId, name: 'شعبه ونک', address: 'تهران', zoneId: 'central', status: 'ACTIVE' }] }), { status: 200 })
      }
      if (url.startsWith('/v1/delivery-requests?page_size=25')) {
        return new Response(JSON.stringify({ items: [], next_cursor: null }), { status: 200 })
      }
      if (url === '/v1/delivery-requests' && init?.method === 'POST') {
        return new Response(JSON.stringify({ delivery_request_id: requestId, status: 'RECEIVED', route_location_coordinates_status: 'CoordinatesPending' }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method || 'GET'} ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('crypto', { randomUUID: () => 'merchant-create-key-001' })
    const user = userEvent.setup()
    render(<App />)

    await user.type(screen.getByLabelText('کلید API'), 'customer-key-for-test')
    await user.click(screen.getByRole('button', { name: 'اتصال به پنل' }))
    expect(await screen.findByRole('heading', { name: 'مدیریت ارسال‌های شهری' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ارسال جدید' }))
    await user.type(screen.getByLabelText('شمارهٔ سفارش کسب‌وکار'), 'ORDER-8421')
    await user.selectOptions(screen.getByLabelText('شعبهٔ مبدا'), branchId)
    await user.type(screen.getByLabelText('نشانی دریافت'), 'تهران، خیابان ونک')
    await user.type(screen.getByLabelText('نشانی گیرنده'), 'تهران، یوسف‌آباد')
    await user.clear(screen.getByLabelText('تعداد کیسه'))
    await user.type(screen.getByLabelText('تعداد کیسه'), '2')
    await user.click(screen.getByRole('button', { name: 'ثبت درخواست ارسال' }))

    expect(await screen.findByRole('status')).toHaveTextContent(requestId)
    expect(screen.getByRole('status')).toHaveTextContent('مختصات هنوز ثبت نشده')
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/delivery-requests', expect.objectContaining({ method: 'POST' })))
    const [, options] = fetchMock.mock.calls.find(([url, init]) => String(url) === '/v1/delivery-requests' && init?.method === 'POST')!
    expect(options?.headers).toMatchObject({
      Authorization: 'Bearer customer-key-for-test',
      'Idempotency-Key': 'merchant-create-key-001',
    })
    expect(JSON.parse(String(options?.body))).toMatchObject({
      external_reference: 'ORDER-8421',
      branch_id: branchId,
      pickup: { address_text: 'تهران، خیابان ونک' },
      dropoff: { address_text: 'تهران، یوسف‌آباد' },
      shipment: { shipment_model: 'BAG_BASED', bag_count: 2, load_class: 'M' },
      requested_service: 'URBAN_DELIVERY',
      requested_priority: 'STANDARD',
    })
    expect(localStorage.length).toBe(0)
  })
})
