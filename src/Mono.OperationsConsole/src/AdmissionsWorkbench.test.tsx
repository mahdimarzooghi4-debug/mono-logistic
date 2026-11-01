import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import AdmissionsWorkbench from './AdmissionsWorkbench'

describe('AdmissionsWorkbench', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals() })

  it('combines real courier and business responses and filters by record type', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      const body = url.includes('/customers?')
        ? { customers: [{ customerId: 'customer-1', legalName: 'فروشگاه نمونه', displayName: 'نمونه', status: 'PENDING', createdAt: '2026-10-20T10:00:00Z' }] }
        : { items: [{ courier_id: 'courier-1', status: 'REGISTERED', mode: 'MOTORCYCLE', created_at: '2026-10-20T10:00:00Z', updated_at: '2026-10-21T10:00:00Z' }] }
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<AdmissionsWorkbench token="token" reloadKey={0} onConnected={vi.fn()} onOpenBusinesses={vi.fn()} onOpenCouriers={vi.fn()} />)

    expect(await screen.findByText('نمونه')).toBeInTheDocument()
    expect(screen.getAllByText('courier-1')).toHaveLength(2)
    expect(screen.getByText('پرونده در این صفحه')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'پیک‌ها' }))
    await waitFor(() => expect(screen.queryByText('نمونه')).not.toBeInTheDocument())
    expect(screen.getAllByText('courier-1')).toHaveLength(2)
  })

  it('renders the Figma admissions overview in demo mode with searchable sample records', async () => {
    render(<AdmissionsWorkbench token="" reloadKey={0} onConnected={vi.fn()} onOpenBusinesses={vi.fn()} onOpenCouriers={vi.fn()} demoMode />)
    expect(screen.getByRole('heading', { name: 'ثبت‌نام و پذیرش مونو' })).toBeInTheDocument()
    expect(screen.getByText('وضعیت پایپ‌لاین')).toBeInTheDocument()
    expect(screen.getByText('مسیر پذیرش پیک')).toBeInTheDocument()
    expect(screen.getByText('CR-REG-۰۱۴۸')).toBeInTheDocument()
    fireEvent.change(screen.getByRole('textbox', { name: 'جست‌وجوی شناسه، نام، موبایل یا کسب‌وکار' }), { target: { value: 'فروشگاه سپهر' } })
    expect(screen.getByText('فروشگاه سپهر')).toBeInTheDocument()
    expect(screen.queryByText('رضا احمدی')).not.toBeInTheDocument()
  })

  it('opens the Figma courier admission review from its internal navigation', async () => {
    render(<AdmissionsWorkbench token="" reloadKey={0} onConnected={vi.fn()} onOpenBusinesses={vi.fn()} onOpenCouriers={vi.fn()} demoMode />)
    fireEvent.click(within(screen.getByRole('navigation', { name: 'منوی پذیرش' })).getByRole('button', { name: /پیک‌ها/ }))
    expect(await screen.findByRole('heading', { name: 'ثبت‌نام و پذیرش پیک‌ها' })).toBeInTheDocument()
    expect(screen.getByText('چک‌لیست پذیرش')).toBeInTheDocument()
    expect(screen.getByText('نتیجهٔ بررسی')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /CR-REG-۰۱۴۲/ }))
    expect(screen.getByRole('heading', { name: /CR-REG-۰۱۴۲ — سعید مرادی/ })).toBeInTheDocument()
  })

  it('opens the Figma business admission review with branches and connection decision', async () => {
    render(<AdmissionsWorkbench token="" reloadKey={0} onConnected={vi.fn()} onOpenBusinesses={vi.fn()} onOpenCouriers={vi.fn()} demoMode />)
    fireEvent.click(within(screen.getByRole('navigation', { name: 'منوی پذیرش' })).getByRole('button', { name: /کسب‌وکارها/ }))
    expect(await screen.findByRole('heading', { name: 'ثبت‌نام و پذیرش کسب‌وکارها' })).toBeInTheDocument()
    expect(screen.getByText('سرویس، شعب و اتصال')).toBeInTheDocument()
    expect(screen.getByText('روش اتصال پس از فعال‌سازی')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /BIZ-REG-۰۰۳۱/ }))
    expect(screen.getByRole('heading', { name: /BIZ-REG-۰۰۳۱ — سوپرمارکت آریا/ })).toBeInTheDocument()
  })
})
