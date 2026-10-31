import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import RoutePlanWorkbench from './RoutePlanWorkbench'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('Operations route plan workbench', () => {
  it('loads the persisted route plan and its scoped read audit without presenting sample routes', async () => {
    const routeId = 'route_tehran_01'
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url === `/operations/v1/routes/${routeId}/plan`) {
        return new Response(JSON.stringify({ version: 2, recordedAt: '2026-10-31T10:00:00Z', snapshot: {
          routeId, startLocationId: 'loc_start_1', startedAt: '2026-10-31T09:00:00Z',
          missions: [{ missionId: 'mission_1', committedDeliveryAt: '2026-10-31T12:00:00Z' }],
          orderedStops: [
            { missionId: 'mission_1', kind: 0, locationId: 'loc_pickup_1', serviceSeconds: 45, pickupReadyAt: '2026-10-31T09:30:00Z' },
            { missionId: 'mission_1', kind: 1, locationId: 'loc_dropoff_1', serviceSeconds: 60, pickupReadyAt: null },
          ],
        } }), { status: 200 })
      }
      if (url.startsWith('/operations/v1/routes/plan-read-audit?')) {
        expect(new URLSearchParams(url.split('?')[1]).get('route_id')).toBe(routeId)
        return new Response(JSON.stringify({ items: [{
          audit_id: 'rpa_00000000000000000000000000000001', actor_id: 'ops-admin', route_id: routeId,
          action: 'ROUTE_PLAN_CURRENT_READ', version: 2, outcome: 'FOUND', result_count: 1,
          occurred_at: '2026-10-31T10:00:00Z',
        }], nextCursor: null }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method || 'GET'} ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<RoutePlanWorkbench token="verified-admin" reloadKey={0} onConnected={vi.fn()} />)

    fireEvent.change(screen.getByLabelText('جست‌وجوی مسیر یا پیک'), { target: { value: routeId } })
    fireEvent.submit(screen.getByLabelText('جست‌وجوی مسیر یا پیک').closest('form')!)

    expect(await screen.findByRole('heading', { name: routeId })).toBeInTheDocument()
    expect(screen.getAllByText('نسخهٔ ۲')).toHaveLength(1)
    expect(screen.getByText('loc_pickup_1')).toBeInTheDocument()
    expect(screen.getByText('loc_dropoff_1')).toBeInTheDocument()
    expect(screen.getByText('ops-admin')).toBeInTheDocument()
    expect(screen.getByText(/فهرست مسیرهای فعال هنوز از API در دسترس نیست/)).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    for (const [, init] of fetchMock.mock.calls) {
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer verified-admin')
    }
  })

  it('shows the API not-found response instead of substituting sample route data', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ error: { code: 'ROUTE_NOT_FOUND', message: 'The Route plan was not found.' } }), { status: 404 })))
    render(<RoutePlanWorkbench token="verified-admin" reloadKey={0} onConnected={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('جست‌وجوی مسیر یا پیک'), { target: { value: 'route_missing' } })
    fireEvent.submit(screen.getByLabelText('جست‌وجوی مسیر یا پیک').closest('form')!)
    expect(await screen.findByRole('alert')).toHaveTextContent('The Route plan was not found.')
    expect(screen.queryByText(/RTE-۰۷۱/)).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'ترتیب توقف‌ها' })).toBeInTheDocument()
    expect(screen.getByText('برای نمایش توقف‌ها، یک مسیر را جست‌وجو کنید.')).toBeInTheDocument()
  })
})
