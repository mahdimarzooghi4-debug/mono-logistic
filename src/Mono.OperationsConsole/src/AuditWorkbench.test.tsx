import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import AuditWorkbench from './AuditWorkbench'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('Operations audit workbench', () => {
  it('reads Mission audit using the selected filters and displays source-backed records', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer verified-admin')
      const url = String(input)
      expect(url).toContain('/operations/v1/mission-read-audit?')
      const query = new URLSearchParams(url.split('?')[1])
      if (query.get('mission_id')) {
        expect(query.get('mission_id')).toBe('mis_test_1')
        expect(query.get('actor_id')).toBe('ops-user')
      }
      return new Response(JSON.stringify({ items: [{
        audit_id: 'mra_00000000000000000000000000000001', actor_id: 'ops-user',
        action: 'MISSION_DETAIL', mission_id: 'mis_test_1', result_count: 1,
        occurred_at: '2026-10-31T10:00:00Z',
      }], next_cursor: null }), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<AuditWorkbench token="verified-admin" reloadKey={0} onConnected={vi.fn()} />)

    expect(await screen.findByText('MISSION_DETAIL')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('شناسهٔ مأموریت'), { target: { value: 'mis_test_1' } })
    fireEvent.change(screen.getByLabelText('عامل'), { target: { value: 'ops-user' } })
    fireEvent.click(screen.getByRole('button', { name: 'اعمال فیلتر' }))

    expect(await screen.findByText('MISSION_DETAIL')).toBeInTheDocument()
    expect(screen.getByText('mis_test_1')).toBeInTheDocument()
    expect(screen.getByText('ops-user')).toBeInTheDocument()
    expect(screen.getByText(/جست‌وجوی یکپارچهٔ همهٔ رویدادها نیست/)).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(fetchMock.mock.calls.some(([url]) => new URLSearchParams(String(url).split('?')[1]).get('mission_id') === 'mis_test_1')).toBe(true)
    expect(new Headers(fetchMock.mock.calls.at(-1)?.[1]?.headers).get('Authorization')).toBe('Bearer verified-admin')
  })

  it('loads route-read audit with the required date range and selected route', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.startsWith('/operations/v1/mission-read-audit?')) {
        return new Response(JSON.stringify({ items: [], next_cursor: null }), { status: 200 })
      }
      expect(url).toContain('/operations/v1/routes/plan-read-audit?')
      const query = new URLSearchParams(url.split('?')[1])
      if (query.get('route_id')) expect(query.get('route_id')).toBe('route_test_1')
      expect(query.get('from_at')).toBeTruthy()
      expect(query.get('to_at')).toBeTruthy()
      return new Response(JSON.stringify({ items: [{
        audit_id: 'rpa_00000000000000000000000000000001', actor_id: 'ops-user', route_id: 'route_test_1',
        action: 'ROUTE_PLAN_CURRENT_READ', version: 1, outcome: 'FOUND', result_count: 1,
        occurred_at: '2026-10-31T10:00:00Z',
      }], nextCursor: null }), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<AuditWorkbench token="verified-admin" reloadKey={0} onConnected={vi.fn()} />)
    fireEvent.click(screen.getByRole('tab', { name: 'مسیرها' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'اعمال فیلتر' })).toBeEnabled())
    fireEvent.change(screen.getByLabelText('شناسهٔ مسیر'), { target: { value: 'route_test_1' } })
    fireEvent.click(screen.getByRole('button', { name: 'اعمال فیلتر' }))
    expect(await screen.findByText('ROUTE_PLAN_CURRENT_READ')).toBeInTheDocument()
    expect(screen.getByText('route_test_1')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
    expect(fetchMock.mock.calls.some(([url]) => new URLSearchParams(String(url).split('?')[1]).get('route_id') === 'route_test_1')).toBe(true)
  })
})
