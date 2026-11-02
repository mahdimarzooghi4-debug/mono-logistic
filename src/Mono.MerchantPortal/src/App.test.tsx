import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('merchant portal delivery flow', () => {
  it('shows a clearly labeled local preview and keeps its sample delivery out of the API', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'مشاهدهٔ نسخهٔ نمایشی' }))
    expect(screen.getByText('محیط نمایشی · داده‌های ساختگی')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'داشبورد کسب‌وکار' })).toBeInTheDocument()

    await user.click(screen.getAllByRole('button', { name: 'ارسال جدید' })[0])
    await user.clear(screen.getByLabelText('شمارهٔ سفارش'))
    await user.type(screen.getByLabelText('شمارهٔ سفارش'), 'ORD-TEST')
    await user.click(screen.getByRole('button', { name: 'ثبت نمونهٔ ارسال' }))

    expect(await screen.findByText('ORD-TEST')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('فقط در همین پیش‌نمایش')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('renders the business dashboard tools from real account data and supports quick tracking', async () => {
    const branchId = `br_${'c'.repeat(32)}`
    const requestId = `drq_${'d'.repeat(32)}`
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url === '/v1/branches') {
        return new Response(JSON.stringify({ branches: [{ branchId, name: 'شعبه ونک', address: 'تهران', latitude: 35.75, longitude: 51.41, zoneId: 'central', status: 'ACTIVE' }] }), { status: 200 })
      }
      if (url.startsWith('/v1/delivery-requests?page_size=25')) {
        return new Response(JSON.stringify({ items: [], next_cursor: null }), { status: 200 })
      }
      if (url === `/v1/delivery-requests/${requestId}`) {
        return new Response(JSON.stringify({ delivery_request_id: requestId, external_reference: 'ORDER-17', status: 'RECEIVED', created_at: '2026-10-23T10:00:00Z', mission_id: null, mission_status: null, committed_delivery_at: null, quote: null, promise: null }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.type(screen.getByLabelText('کلید API'), 'customer-key-for-test')
    await user.click(screen.getByRole('button', { name: 'اتصال به پنل' }))
    expect(await screen.findByRole('heading', { name: 'وضعیت شعب' })).toBeInTheDocument()
    expect(screen.getByText('شعبه ونک')).toBeInTheDocument()
    expect(screen.getByText('ارسال شهری')).toBeInTheDocument()
    expect(screen.getByText('آخرین به‌روزرسانی')).toBeInTheDocument()
    expect(screen.getByText('گزارش استثناها در API این پنل موجود نیست')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /انتخاب شعبه/ })).toBeInTheDocument()

    await user.type(screen.getByLabelText('شناسهٔ ارسال یا مأموریت'), requestId)
    await user.click(screen.getByRole('button', { name: 'جست‌وجو' }))
    expect(await screen.findByRole('dialog')).toHaveTextContent('ORDER-17')
    expect(fetchMock).toHaveBeenCalledWith(`/v1/delivery-requests/${requestId}`, expect.objectContaining({ cache: 'no-store' }))
  })

  it('connects with a customer token and creates a scoped, idempotent delivery request', async () => {
    const branchId = `br_${'a'.repeat(32)}`
    const requestId = `drq_${'b'.repeat(32)}`
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url === '/v1/branches') {
        return new Response(JSON.stringify({ branches: [{ branchId, name: 'شعبه ونک', address: 'تهران', latitude: 35.75, longitude: 51.41, zoneId: 'central', status: 'ACTIVE' }] }), { status: 200 })
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
    expect(await screen.findByRole('heading', { name: 'داشبورد کسب‌وکار' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ارسال جدید' }))
    expect(screen.getByRole('navigation', { name: 'مراحل ثبت ارسال' })).toHaveTextContent('امکان‌سنجی و قیمت')
    expect(screen.getByText('هنوز محاسبه نشده')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'ذخیره پیش‌نویس' })).toBeDisabled()
    await user.type(screen.getByLabelText('شناسه سفارش داخلی'), 'ORDER-8421')
    await user.selectOptions(screen.getByLabelText('شعبهٔ مبدا'), branchId)
    await user.type(screen.getByLabelText('آدرس مبدا'), 'تهران، خیابان ونک')
    await user.type(screen.getByLabelText('آدرس مقصد'), 'تهران، یوسف‌آباد')
    await user.type(screen.getByLabelText('عرض جغرافیایی مقصد'), '35.70')
    await user.click(screen.getByRole('button', { name: 'ثبت درخواست برای بررسی' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('طول و عرض جغرافیایی را هر دو وارد کنید')
    expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/v1/delivery-requests' && init?.method === 'POST')).toBe(false)
    await user.type(screen.getByLabelText('طول جغرافیایی مقصد'), '51.40')
    await user.clear(screen.getByLabelText('تعداد کیسه'))
    await user.type(screen.getByLabelText('تعداد کیسه'), '2')
    await user.type(screen.getByLabelText('وزن تقریبی (کیلوگرم)'), '2.5')
    await user.click(screen.getByRole('button', { name: 'ثبت درخواست برای بررسی' }))

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
      pickup: { address_text: 'تهران، خیابان ونک', latitude: 35.75, longitude: 51.41 },
      dropoff: { address_text: 'تهران، یوسف‌آباد', latitude: 35.7, longitude: 51.4 },
      shipment: { shipment_model: 'BAG_BASED', bag_count: 2, estimated_weight_kg: 2.5, load_class: 'M' },
      requested_service: 'URBAN_DELIVERY',
      requested_priority: 'STANDARD',
    })
    expect(localStorage.length).toBe(0)
  })
})
