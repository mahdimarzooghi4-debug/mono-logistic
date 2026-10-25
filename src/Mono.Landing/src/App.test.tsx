import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

function stubRandomUuid(value: string) {
  const original = globalThis.crypto
  vi.stubGlobal('crypto', new Proxy(original, {
    get(target, property) {
      if (property === 'randomUUID') return () => value
      const result = Reflect.get(target, property, target)
      return typeof result === 'function' ? result.bind(target) : result
    },
  }))
}

describe('Mono public landing', () => {
  it('renders the hero, registration choices, and footer navigation', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: /مسیرها تغییر می‌کنند/ })).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'شروع همکاری' })[0]).toHaveAttribute('href', '#registration')
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'برای سفیران' })[0]).toHaveAttribute('href', '#courier')
  })
  it('switches between business and courier forms', async () => {
    const user = userEvent.setup()
    render(<App />)
    expect(screen.getByLabelText('نام کسب‌وکار')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'ثبت‌نام سفیران' }))
    expect(screen.queryByLabelText('نام کسب‌وکار')).not.toBeInTheDocument()
    expect(screen.getByRole('form', { name: 'فرم ثبت‌نام سفیر' })).toBeInTheDocument()
  })
  it('shows the versioned privacy notice and requires consent before submission', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)
    expect(screen.getByRole('heading', { name: 'اطلاعیهٔ حریم خصوصی درخواست همکاری' })).toBeInTheDocument()
    await user.type(screen.getByLabelText('نام و نام خانوادگی'), 'کاربر آزمایشی')
    await user.type(screen.getByLabelText('شمارهٔ همراه'), '09121234567')
    await user.type(screen.getByLabelText('نام کسب‌وکار'), 'فروشگاه آزمایشی')
    await user.click(screen.getByRole('button', { name: 'ارسال درخواست همکاری' }))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(screen.getByRole('checkbox')).toBeRequired()
  })

  it('submits an explicitly consented application with the displayed notice version', async () => {
    stubRandomUuid('00000000-0000-4000-8000-000000000001')
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requestId: 'reg_0123456789abcdef0123456789abcdef', status: 'SUBMITTED' }), { status: 202 }))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('نام و نام خانوادگی'), 'کاربر آزمایشی')
    await user.type(screen.getByLabelText('شمارهٔ همراه'), '09121234567')
    await user.type(screen.getByLabelText('نام کسب‌وکار'), 'فروشگاه آزمایشی')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'ارسال درخواست همکاری' }))
    expect(await screen.findByRole('status')).toHaveTextContent('این ثبت، حساب یا همکاری فعال ایجاد نمی‌کند')
    const [url, request] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/v1/public-registration-requests')
    expect(request.headers).toMatchObject({ 'Idempotency-Key': '00000000-0000-4000-8000-000000000001' })
    expect(JSON.parse(String(request.body))).toMatchObject({
      type: 'BUSINESS', full_name: 'کاربر آزمایشی', phone: '09121234567',
      business_name: 'فروشگاه آزمایشی', privacy_notice_version: '2026-10-25-v1', consent: true,
    })
  })

  it('keeps the intake closed when the backend is not configured', async () => {
    stubRandomUuid('00000000-0000-4000-8000-000000000002')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Registration is temporarily unavailable.' }), { status: 503 })))
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('نام و نام خانوادگی'), 'کاربر آزمایشی')
    await user.type(screen.getByLabelText('شمارهٔ همراه'), '09121234567')
    await user.type(screen.getByLabelText('نام کسب‌وکار'), 'فروشگاه آزمایشی')
    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: 'ارسال درخواست همکاری' }))
    expect(await screen.findByRole('status')).toHaveTextContent('ثبت درخواست همکاری هنوز برای این محیط فعال نشده است.')
  })
})
