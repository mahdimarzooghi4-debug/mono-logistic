import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import DashboardSummaryCards from './DashboardSummaryCards'

const summary = {
  as_of: '2026-10-23T12:00:00Z', business_date: '2026-10-23', sla_horizon_minutes: 15,
  active_mission_count: 8, unassigned_mission_count: 3, online_courier_count: 5,
  sla_at_risk_count: 2, sla_overdue_count: 1, delivered_today_count: 4,
  on_time_delivered_today_count: 3, on_time_delivery_rate_percent: 75,
}

describe('DashboardSummaryCards', () => {
  afterEach(() => cleanup())

  it('shows system-wide values and routes operational KPIs to their existing panels', () => {
    const onOpenPresence = vi.fn()
    const onOpenDispatch = vi.fn()
    const onOpenSla = vi.fn()
    const onOpenMissions = vi.fn()
    render(<DashboardSummaryCards summary={summary} onOpenPresence={onOpenPresence} onOpenDispatch={onOpenDispatch} onOpenSla={onOpenSla} onOpenMissions={onOpenMissions} />)

    expect(screen.getByText('۷۵٪')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'مشاهدهٔ فهرست وضعیت پیک‌ها' }))
    fireEvent.click(screen.getByRole('button', { name: 'رفتن به صف مأموریت‌های بدون تخصیص' }))
    fireEvent.click(screen.getByRole('button', { name: 'مشاهدهٔ مأموریت‌های دارای هشدار SLA' }))
    fireEvent.click(screen.getByRole('button', { name: 'مشاهدهٔ فهرست مأموریت‌ها' }))

    expect(onOpenPresence).toHaveBeenCalledOnce()
    expect(onOpenDispatch).toHaveBeenCalledOnce()
    expect(onOpenSla).toHaveBeenCalledOnce()
    expect(onOpenMissions).toHaveBeenCalledOnce()
  })

  it('leaves the delivery rate blank when no authoritative summary is available', () => {
    render(<DashboardSummaryCards summary={null} onOpenPresence={vi.fn()} onOpenDispatch={vi.fn()} onOpenSla={vi.fn()} onOpenMissions={vi.fn()} />)
    expect(screen.getByText('تحویل به‌موقع امروز').parentElement).toHaveTextContent('—')
    expect(screen.getAllByText('در انتظار دادهٔ API')).toHaveLength(3)
  })
})
