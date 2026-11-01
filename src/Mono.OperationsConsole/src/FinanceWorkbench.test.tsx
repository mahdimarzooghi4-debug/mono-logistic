import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import FinanceWorkbench, { type MissionFare } from './FinanceWorkbench'

afterEach(cleanup)

const props = {
  items: [] as MissionFare[], loading: false, error: '', token: '', query: '', status: '', pageIndex: 0, hasNext: false,
  onQueryChange: vi.fn(), onStatusChange: vi.fn(), onSearch: vi.fn(), onPrevious: vi.fn(), onNext: vi.fn(), onRefresh: vi.fn(),
}

describe('FinanceWorkbench', () => {
  it('keeps the Figma finance layout and real settlement data empty without an API token', () => {
    render(<FinanceWorkbench {...props} />)
    expect(screen.getByRole('heading', { name: 'مالی و تسویه' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'صورت‌حساب حنا' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'دفتر مالی مأموریت' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'کیف پول پیک' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'پرداخت‌ها' })).toBeInTheDocument()
    expect(screen.getByText('برای مشاهدهٔ دادهٔ واقعی، به API عملیات متصل شوید.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'ثبت پرداخت' })).toBeDisabled()
    expect(screen.getByText('پرداخت در انتظار').parentElement).toHaveTextContent('—')
  })

  it('renders only fare snapshot amounts and does not claim a real payout', () => {
    const item: MissionFare = {
      mission_id: 'mis_0123456789abcdef0123456789abcdef', customer_id: 'cus_123', delivery_request_id: 'req_123',
      status: 'DELIVERED', committed_delivery_at: '2026-11-01T09:00:00Z', created_at: '2026-11-01T08:00:00Z',
      quote_id: 'quo_123', quote_version: 2, distance_meters: 1500, fare_toman: 100000, buyer_share_toman: 10000,
      seller_share_toman: 90000, mono_commission_estimate_toman: 10000, commission_vat_estimate_toman: 1000, courier_net_estimate_toman: 89000,
    }
    render(<FinanceWorkbench {...props} token="ops-token" items={[item]} />)
    expect(screen.getAllByText('۱۰۰٬۰۰۰ تومان').length).toBeGreaterThan(0)
    expect(screen.getAllByText('۸۹٬۰۰۰ تومان').length).toBeGreaterThan(0)
    expect(screen.getByText('تاریخچهٔ پرداخت در API مالی موجود نیست.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'بازبینی تطبیق' })).toBeDisabled()
  })
})
