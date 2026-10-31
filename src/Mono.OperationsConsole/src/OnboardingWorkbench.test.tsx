import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import OnboardingWorkbench from './OnboardingWorkbench'

const customer = { customerId: 'cus_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', legalName: 'فروشگاه نمونه', displayName: 'نمونه', status: 'PENDING', customerType: 'MERCHANT', defaultChannel: 'B2B_API', defaultCurrency: 'IRT', createdAt: '2026-10-21T10:00:00Z' }
const branch = { branchId: 'brn_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', customerId: customer.customerId, name: 'شعبه مرکزی', address: 'تهران', latitude: 35.7, longitude: 51.4, zoneId: 'zone-1', status: 'ACTIVE', operatingHours: { weekdays: '09:00-17:00' } }
const ok = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('OnboardingWorkbench', () => {
  beforeEach(() => { vi.stubGlobal('fetch', vi.fn()); vi.mocked(fetch).mockResolvedValue(ok({ customers: [customer], nextCursor: null })) })
  afterEach(() => { cleanup(); vi.unstubAllGlobals() })

  it('lists API customer records and loads customer audit and branch registry', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const url = String(input)
      if (url.includes('/api-keys')) return ok({ apiKeys: [{ keyId: 'mk_live_abc123', customerId: customer.customerId, scopes: ['deliveries:write'], createdAt: '2026-10-20T10:00:00Z', expiresAt: '2027-10-20T10:00:00Z', revokedAt: null, secret: 'must-never-render' }] })
      if (url.includes('/audit')) return ok({ entries: [{ auditId: 'aud_1', action: 'CREATED', actorId: 'admin', reason: 'verified', occurredAt: '2026-10-21T10:00:00Z' }] })
      if (url.endsWith('/branches')) return ok({ branches: [branch] })
      return ok({ customers: [customer], nextCursor: null })
    })
    render(<OnboardingWorkbench token="admin-token" reloadKey={0} onConnected={() => undefined} />)
    expect(await screen.findByText('نمونه')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /نمونه/ }))
    expect(await screen.findByText('شعبه مرکزی')).toBeInTheDocument()
    expect(await screen.findByText('mk_live_abc123')).toBeInTheDocument()
    expect(screen.getByText('deliveries:write')).toBeInTheDocument()
    expect(screen.queryByText('must-never-render')).not.toBeInTheDocument()
    expect(screen.getByText(/admin · verified/)).toBeInTheDocument()
    const listCall = vi.mocked(fetch).mock.calls.find(([input]) => String(input).includes('/operations/v1/customers?'))
    expect(new Headers(listCall?.[1]?.headers).get('Authorization')).toBe('Bearer admin-token')
  })

  it('creates a pending customer with a required reason and sends no credentials', async () => {
    const user = userEvent.setup()
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      if (String(input).endsWith('/operations/v1/customers') && init?.method === 'POST') return ok({ ...customer, customerId: 'cus_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' }, 201)
      if (String(input).includes('/audit')) return ok({ entries: [] })
      if (String(input).endsWith('/branches')) return ok({ branches: [] })
      return ok({ customers: [customer], nextCursor: null })
    })
    render(<OnboardingWorkbench token="admin-token" reloadKey={0} onConnected={() => undefined} />)
    await user.click(await screen.findByRole('button', { name: 'ثبت مشتری' }))
    await user.type(screen.getByLabelText('نام حقوقی'), 'تجارت جدید')
    await user.type(screen.getByLabelText('دلیل ثبت'), 'پرونده تأیید شده')
    await user.click(screen.getByRole('button', { name: 'ثبت Pending' }))
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === 'POST')).toBe(true))
    const call = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === 'POST')
    const payload = JSON.parse(String(call?.[1]?.body))
    expect(payload).toMatchObject({ legalName: 'تجارت جدید', reason: 'پرونده تأیید شده', customerType: 'MERCHANT', defaultChannel: 'B2B_API' })
    expect(payload).not.toHaveProperty('apiKey')
    expect(await screen.findByRole('status')).toHaveTextContent(/API key یا دسترسی صادر نمی‌کند/)
  })

  it('requires explicit reason confirmation for activation and records it in the API request', async () => {
    const user = userEvent.setup()
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      if (String(input).includes('/audit')) return ok({ entries: [] })
      if (String(input).endsWith('/branches')) return ok({ branches: [] })
      if (String(input).includes('/activate') && init?.method === 'POST') return ok(undefined, 204)
      return ok({ customers: [customer], nextCursor: null })
    })
    render(<OnboardingWorkbench token="admin-token" reloadKey={0} onConnected={() => undefined} />)
    await user.click(await screen.findByRole('button', { name: /نمونه/ }))
    await user.click(await screen.findByRole('button', { name: 'درخواست فعال‌سازی' }))
    const confirm = screen.getByRole('button', { name: 'تأیید و فعال‌سازی' })
    expect(confirm).toBeDisabled()
    await user.type(screen.getByLabelText('دلیل ممیزی‌شده'), 'قرارداد تکمیل شد')
    await user.click(confirm)
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).includes('/activate'))).toBe(true))
    const call = vi.mocked(fetch).mock.calls.find(([url]) => String(url).includes('/activate'))
    expect(JSON.parse(String(call?.[1]?.body))).toEqual({ reason: 'قرارداد تکمیل شد' })
  })

  it('rejects malformed operating-hours JSON locally and creates a branch only after selection', async () => {
    const user = userEvent.setup()
    vi.mocked(fetch).mockImplementation(async (input) => {
      if (String(input).includes('/audit')) return ok({ entries: [] })
      if (String(input).endsWith('/branches')) return ok({ branches: [] })
      return ok({ customers: [{ ...customer, status: 'ACTIVE' }], nextCursor: null })
    })
    render(<OnboardingWorkbench token="admin-token" reloadKey={0} onConnected={() => undefined} />)
    await user.click(await screen.findByRole('button', { name: /نمونه/ }))
    await user.click(await screen.findByRole('button', { name: 'افزودن شعبه' }))
    await user.type(screen.getByLabelText('نام شعبه'), 'شعبه دوم')
    fireEvent.change(screen.getByLabelText('ساعات کاری (JSON object)'), { target: { value: '{bad json' } })
    await user.type(screen.getByLabelText('دلیل الزامی'), 'ثبت شعبه')
    await user.click(screen.getByRole('button', { name: 'ذخیره از طریق API' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('ساعات کاری باید JSON معتبر باشد')
    expect(vi.mocked(fetch).mock.calls.some(([url, init]) => String(url).endsWith('/branches') && init?.method === 'POST')).toBe(false)
  })

  it('creates an active-customer branch with its audit reason and displays the API response', async () => {
    const user = userEvent.setup()
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = String(input)
      if (url.includes('/branches/') && url.includes('/audit')) return ok({ entries: [{ auditId: 'ba_1', action: 'CREATED', actorId: 'admin', reason: 'ثبت شعبه', occurredAt: '2026-10-21T10:00:00Z' }] })
      if (url.endsWith('/branches') && init?.method === 'POST') { const { operatingHours: _hours, ...summary } = branch; return ok(summary, 201) }
      if (url.includes('/branches/') && !url.endsWith('/audit')) return ok(branch)
      if (url.endsWith('/branches')) return ok({ branches: [] })
      if (url.includes('/audit')) return ok({ entries: [] })
      return ok({ customers: [{ ...customer, status: 'ACTIVE' }], nextCursor: null })
    })
    render(<OnboardingWorkbench token="admin-token" reloadKey={0} onConnected={() => undefined} />)
    await user.click(await screen.findByRole('button', { name: /نمونه/ }))
    await user.click(await screen.findByRole('button', { name: 'افزودن شعبه' }))
    await user.type(screen.getByLabelText('نام شعبه'), 'شعبه مرکزی')
    await user.type(screen.getByLabelText('دلیل الزامی'), 'ثبت شعبه')
    await user.click(screen.getByRole('button', { name: 'ذخیره از طریق API' }))
    expect(await screen.findByText('zone-1')).toBeInTheDocument()
    expect(screen.getByText(/weekdays/)).toBeInTheDocument()
    expect(await screen.findByText(/admin · ثبت شعبه/)).toBeInTheDocument()
    const call = vi.mocked(fetch).mock.calls.find(([url, init]) => String(url).endsWith('/branches') && init?.method === 'POST')
    expect(JSON.parse(String(call?.[1]?.body))).toMatchObject({ name: 'شعبه مرکزی', reason: 'ثبت شعبه', operatingHours: {} })
  })

  it('shows administrator and MFA denial returned by the API without placeholder rows', async () => {
    vi.mocked(fetch).mockResolvedValue(ok({ error: { code: 'FORBIDDEN' } }, 403))
    render(<OnboardingWorkbench token="not-admin" reloadKey={0} onConnected={() => undefined} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('مدیر Mono با MFA')
    expect(screen.getByText('برای این فیلتر موردی در API برنگشت.')).toBeInTheDocument()
  })
})
