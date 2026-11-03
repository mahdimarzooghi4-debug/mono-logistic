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
    if (url === '/v1/delivery-requests' && init?.method === 'POST') return new Response(JSON.stringify({ delivery_request_id: requestId }), { status: 200 })
    if (url.endsWith('/confirm') && init?.method === 'POST') return new Response(JSON.stringify({ mission_id: 'mission-real-api-response' }), { status: 200 })
    return new Response(JSON.stringify({ error: { code: 'NOT_FOUND' } }), { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.history.replaceState({}, '', '/') })

describe('Mono merchant mobile P0', () => {
  it('shows the Figma mobile preview flow without calling the API', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)
    expect(screen.getByText('یک ارسال در معرض دیرکرد')).toBeInTheDocument()
    expect(screen.getByText('سفارش ۸۴۲۱ • تعهد تا ۱۸:۴۵')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ارسال‌ها' }))
    await user.click(screen.getByRole('tab', { name: 'تکمیل‌شده' }))
    await user.type(screen.getByLabelText('جست‌وجوی سفارش'), 'میرداماد')
    const completedOrder = screen.getByRole('button', { name: /سفارش ۸۴۱۸/ })
    expect(completedOrder).toHaveTextContent('تحویل‌شده')
    await user.click(completedOrder)
    expect(screen.getByRole('heading', { name: 'جزئیات ارسال' })).toBeInTheDocument()
    expect(screen.getByText('تحویل به گیرنده انجام شد')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ارسال‌ها' }))
    await user.click(screen.getByRole('tab', { name: 'ناموفق' }))
    await user.clear(screen.getByLabelText('جست‌وجوی سفارش'))
    const failedOrder = screen.getByRole('button', { name: /سفارش ۸۴۱۲/ })
    expect(failedOrder).toHaveTextContent('ناموفق')
    await user.click(failedOrder)
    expect(screen.getByText('ارسال ناموفق شد')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ارسال‌ها' }))
    await user.click(screen.getByRole('tab', { name: 'فعال' }))
    await user.click(screen.getByRole('button', { name: 'خانه' }))

    await user.click(screen.getByRole('button', { name: 'ثبت ارسال جدید' }))
    expect(screen.getByRole('heading', { name: 'ثبت ارسال جدید' })).toBeInTheDocument()
    await user.clear(screen.getByLabelText('نام گیرنده'))
    await user.type(screen.getByLabelText('نام گیرنده'), 'علی رضایی')
    await user.clear(screen.getByLabelText('نشانی مقصد'))
    await user.type(screen.getByLabelText('نشانی مقصد'), 'میدان ونک')
    await user.click(screen.getByRole('button', { name: 'بررسی هزینه و زمان' }))
    expect(screen.getByRole('heading', { name: 'پیشنهاد مونو' })).toBeInTheDocument()
    expect(screen.getByText('۴۲٬۵۰۰ تومان')).toBeInTheDocument()
    expect(screen.getByText('علی رضایی')).toBeInTheDocument()
    expect(screen.getByText('میدان ونک')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'تأیید و ثبت ارسال' }))
    expect(screen.getByText('سفارش ۸۴۳۱')).toBeInTheDocument()
    expect(screen.getAllByText('در انتظار تخصیص پیک').length).toBeGreaterThan(0)
    expect(screen.getByText('درخواست نمایشی تأیید شد؛ هنوز به سرور ثبت نشده است.')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('filters demo notifications, opens linked orders, and marks read items', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'اعلان‌ها' }))
    expect(screen.getByRole('heading', { name: 'اعلان‌ها' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'نیازمند اقدام' }))
    expect(screen.getByRole('button', { name: 'ریسک دیرکرد برای سفارش ۸۴۲۱' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'سفارش ۸۴۳۰ به پیک تخصیص داده شد' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'ریسک دیرکرد برای سفارش ۸۴۲۱' }))
    expect(screen.getByRole('heading', { name: 'جزئیات ارسال' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'اعلان‌ها' }))
    await user.click(screen.getByRole('tab', { name: 'اطلاع‌رسانی' }))
    await user.click(screen.getByRole('button', { name: 'صورتحساب هفتگی صادر شد' }))
    expect(screen.getByRole('status')).toHaveTextContent('فقط نمونهٔ نمایشی')
    expect(screen.getByLabelText('خوانده شده')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('explains a delivery exception and returns to its shipment', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: /یک ارسال در معرض دیرکرد/ }))
    await user.click(screen.getByRole('button', { name: 'مشاهده اقدام‌ها' }))
    expect(screen.getByText('استثنای ارسال')).toBeInTheDocument()
    expect(screen.getByText('۱۲ دقیقه ریسک دیرکرد')).toBeInTheDocument()
    expect(screen.getByText('کندی مسیر و تراکم ترافیکی')).toBeInTheDocument()
    expect(screen.getByText('پایش مسیر جایگزین و کنترل تعهد')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'تماس با پشتیبانی' }))
    expect(screen.getByRole('status')).toHaveTextContent('پشتیبانی')
    await user.click(screen.getByRole('button', { name: 'بستن پیام' }))
    await user.click(screen.getByRole('button', { name: 'بازگشت به ارسال' }))
    expect(screen.getByRole('heading', { name: 'جزئیات ارسال' })).toBeInTheDocument()
  })

  it('selects an available branch and keeps an inactive branch unavailable', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'ونک ⌄' }))
    expect(screen.getByText('انتخاب شعبه')).toBeInTheDocument()
    expect(screen.getByText('فروشگاه سپهر')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /شعبه ونک، انتخاب‌شده/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /پونک.*موقتاً غیرفعال/ })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: /جردن.*۲ ارسال فعال/ }))
    expect(screen.getByText('شعبه جردن')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /سفارش ۸۴۵۲/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /سفارش ۸۴۲۱/ })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ارسال‌ها' }))
    expect(screen.getByRole('button', { name: /سفارش ۸۴۵۲/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /سفارش ۸۴۲۱/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'جردن ⌄' }))
    expect(screen.getByRole('button', { name: /شعبه جردن، انتخاب‌شده/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /پونک.*موقتاً غیرفعال/ })).toBeDisabled()
  })

  it('opens the Figma financial summary from More with clearly marked demo amounts', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'بیشتر' }))
    expect(screen.getByRole('button', { name: /مالی، خلاصه هزینه‌ها/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /مالی، خلاصه هزینه‌ها/ }))

    expect(screen.getByRole('heading', { name: 'خلاصه این ماه' })).toBeInTheDocument()
    expect(screen.getByText('۳٬۸۴۰٬۰۰۰', { exact: false })).toBeInTheDocument()
    expect(screen.getByText('۳٬۲۰۰٬۰۰۰', { exact: false })).toBeInTheDocument()
    expect(screen.getByText('۶۴۰٬۰۰۰', { exact: false })).toBeInTheDocument()
    expect(screen.getByText('سفارش ۸۴۳۰')).toBeInTheDocument()
    expect(screen.getByText('سفارش ۸۴۱۵')).toBeInTheDocument()
    expect(screen.getByText(/مبالغ این صفحه نمونهٔ نمایشی هستند/)).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('opens the Figma profile from More and shows demo account details', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'بیشتر' }))
    await user.click(screen.getByRole('button', { name: /پروفایل، مشخصات فردی و نقش/ }))
    expect(screen.getByRole('heading', { name: 'پروفایل' })).toBeInTheDocument()
    expect(screen.getByText('مهدی کاظمی')).toBeInTheDocument()
    expect(screen.getByText('۰۹۱۲ ۱۲۳ ۴۵۶۷')).toBeInTheDocument()
    expect(screen.getByText('m.kazemi@example.com')).toBeInTheDocument()
    expect(screen.getByText('فروشگاه سپهر')).toBeInTheDocument()
    expect(screen.getByText('ونک')).toBeInTheDocument()
    expect(screen.getByText(/اطلاعات این صفحه نمونهٔ نمایشی هستند/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'ویرایش مشخصات' }))
    expect(screen.getByRole('status')).toHaveTextContent('ویرایش اطلاعات حساب')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('opens Figma account settings from More and routes the default branch setting', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'بیشتر' }))
    await user.click(screen.getByText('تنظیمات', { exact: true }))
    expect(screen.getByRole('heading', { level: 1, name: 'تنظیمات' })).toBeInTheDocument()
    expect(screen.getByText('حساب و برنامه')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /امنیت و نشست‌ها، دستگاه‌ها/ })).toBeInTheDocument()
    expect(screen.getByText('مدیریت اعضا، دسترسی‌ها، اتصال‌ها و هویت بصری از پنل وب انجام می‌شود.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /شعبه پیش‌فرض، شعبه ونک/ }))
    expect(screen.getByText('انتخاب شعبه')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('opens support from a delivery and submits the demo request form', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: /یک ارسال در معرض دیرکرد/ }))
    await user.click(screen.getByRole('button', { name: 'پشتیبانی' }))
    expect(screen.getByRole('heading', { name: 'زمینه ارسال به‌صورت خودکار متصل شد' })).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'سفارش ۸۴۲۱' })).getByText('سفارش ۸۴۲۱')).toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: 'موضوع دیگر' }))
    await user.type(screen.getByLabelText('توضیح کوتاه'), 'پیگیری زمان تحویل')
    await user.click(screen.getByRole('button', { name: 'ثبت درخواست پشتیبانی' }))
    expect(screen.getByRole('status')).toHaveTextContent('درخواست پشتیبانی نمایشی ثبت شد')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('opens notification preferences and toggles operational and financial alerts', async () => {
    window.history.replaceState({}, '', '/?demo=1')
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: 'بیشتر' }))
    await user.click(screen.getByText('تنظیمات', { exact: true }))
    await user.click(screen.getByRole('button', { name: /اعلان‌ها، انتخاب اعلان‌های عملیاتی و مالی/ }))
    expect(screen.getByRole('heading', { name: 'چه اعلان‌هایی دریافت کنید؟' })).toBeInTheDocument()
    expect(screen.getByText('عملیاتی')).toBeInTheDocument()

    const riskSwitch = screen.getByRole('switch', { name: 'ریسک دیرکرد و استثنا' })
    const financeSwitch = screen.getByRole('switch', { name: 'مالی' })
    expect(riskSwitch).toHaveAttribute('aria-checked', 'true')
    expect(financeSwitch).toHaveAttribute('aria-checked', 'false')
    await user.click(financeSwitch)
    expect(financeSwitch).toHaveAttribute('aria-checked', 'true')
    await user.click(riskSwitch)
    expect(riskSwitch).toHaveAttribute('aria-checked', 'false')
    expect(screen.getByText(/اعلان‌های بحرانی عملیاتی حتی در حالت کم‌صدا نمایش داده می‌شوند/)).toBeInTheDocument()
  })

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
})
