import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import SlaExceptionsWorkbench from './SlaExceptionsWorkbench'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('SlaExceptionsWorkbench', () => {
  it('keeps counts blank until the operations API is authenticated', () => {
    render(<SlaExceptionsWorkbench token="" onConnected={vi.fn()} onOpenMission={vi.fn()} />)
    expect(screen.getByText('برای شروع به API عملیات متصل شوید.')).toBeInTheDocument()
    expect(screen.getByText('از موعد گذشته').parentElement).toHaveTextContent('—')
    expect(screen.getByRole('button', { name: 'ثبت استثنا' })).toBeDisabled()
  })

  it('keeps alert horizon controls behind the Figma settings action', () => {
    render(<SlaExceptionsWorkbench token="" onConnected={vi.fn()} onOpenMission={vi.fn()} />)
    expect(screen.queryByLabelText('افق پایش')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'تنظیم هشدارها' }))
    expect(screen.getByLabelText('افق پایش')).toBeInTheDocument()
  })

  it('loads actual alert, mission and timeline data without inventing ETA values', async () => {
    const alert = { mission_id: 'mis_0123456789abcdef0123456789abcdef', customer_id: 'cus_123', status: 'ACCEPTED', committed_delivery_at: '2026-10-31T15:00:00Z', courier_id: null, is_overdue: true }
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/sla/delivery-alerts')) return { ok: true, json: async () => ({ items: [alert], next_cursor: null, evaluated_at: '2026-10-31T15:01:00Z' }) }
      if (url.endsWith('/timeline')) return { ok: true, json: async () => ({ events: [{ eventId: 'evt_1', eventType: 'COURIER_OFFER_ACCEPTED', sequence: 1, occurredAt: '2026-10-31T14:00:00Z' }] }) }
      return { ok: true, json: async () => ({ missionId: alert.mission_id, customerId: alert.customer_id, status: alert.status, committedDeliveryAt: alert.committed_delivery_at, createdAt: '2026-10-31T13:00:00Z' }) }
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<SlaExceptionsWorkbench token="ops-token" onConnected={vi.fn()} onOpenMission={vi.fn()} />)
    expect((await screen.findAllByText(alert.mission_id)).length).toBeGreaterThan(0)
    expect(await screen.findByText('پذیرش مأموریت')).toBeInTheDocument()
    expect(screen.getAllByText('زمان واقعی تحویل')).toHaveLength(2)
    expect(screen.getByText('داده‌ای ارائه نشده')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
    expect(screen.getByRole('button', { name: 'تأیید تخصیص مجدد' })).toBeDisabled()
  })
})
