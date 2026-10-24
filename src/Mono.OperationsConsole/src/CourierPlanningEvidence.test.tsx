import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import CourierPlanningEvidence from './CourierPlanningEvidence'

describe('CourierPlanningEvidence', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals() })

  it('loads audited planning evidence with the selected filters and paginates without treating presence as dispatch authority', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'https://mono.test')
      if (url.searchParams.get('cursor')) {
        return new Response(JSON.stringify({
          zone_id: 'zone-central', from: '2026-10-24T09:00:00.000Z', to: '2026-10-24T10:00:00.000Z',
          classification: 'PLANNED_CAPACITY_AND_LIVE_PRESENCE_SIGNALS',
          items: [{ courier_id: 'crr_' + 'b'.repeat(32), mode: 'BIKE', trust_status: 'ACTIVE', operational_status: 'ACTIVE',
            zone_approved: true, has_scheduled_window: false, planned_capacity_contributor: false, exclusion_reasons: ['NO_SCHEDULED_WINDOW'],
            is_online: false, live_presence_zone_id: null, live_presence_updated_at: null, live_presence_candidate: false,
            live_presence_exclusion_reasons: ['COURIER_NOT_ONLINE'] }],
          next_cursor: null,
        }), { status: 200 })
      }
      return new Response(JSON.stringify({
        zone_id: 'zone-central', from: '2026-10-24T09:00:00.000Z', to: '2026-10-24T10:00:00.000Z',
        classification: 'PLANNED_CAPACITY_AND_LIVE_PRESENCE_SIGNALS',
        items: [{ courier_id: 'crr_' + 'a'.repeat(32), mode: 'BIKE', trust_status: 'ACTIVE', operational_status: 'ACTIVE',
          zone_approved: true, has_scheduled_window: true, planned_capacity_contributor: true, exclusion_reasons: [],
          is_online: true, live_presence_zone_id: 'zone-central', live_presence_updated_at: '2026-10-24T09:30:00.000Z',
          live_presence_candidate: true, live_presence_exclusion_reasons: [] }],
        next_cursor: 'crr_' + 'a'.repeat(32),
      }), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<CourierPlanningEvidence token="stage-admin-token" zoneId="zone-central" fromLocal="2026-10-24T09:00" toLocal="2026-10-24T10:00" refreshKey={1} />)

    expect(await screen.findByText('شواهد برنامه‌ریزی و حضور گزارش‌شده')).toBeInTheDocument()
    expect(await screen.findByText('آنلاین گزارش‌شده')).toBeInTheDocument()
    expect(screen.getByText('حضور بر اساس آخرین وضعیت گزارش‌شدهٔ پیک نمایش داده می‌شود؛ زمان آن را بررسی کنید. این شواهد به‌تنهایی حضور فیزیکی، ظرفیت قابل‌تخصیص یا مجوز اعزام را اثبات نمی‌کند.')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('zone_id=zone-central'), expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer stage-admin-token' }),
      cache: 'no-store',
    }))

    fireEvent.click(screen.getByRole('button', { name: 'بعدی' }))
    expect(await screen.findByText('crr_' + 'b'.repeat(32))).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    expect(fetchMock.mock.calls[1][0]).toContain('cursor=crr_')
  })
})
