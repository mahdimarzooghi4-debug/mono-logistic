import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import WebhookEndpointWorkbench from './WebhookEndpointWorkbench'

const endpoint = { endpoint_id: 'whk_abc123', url: 'https://merchant.example/events', events: ['mission.delivered'], status: 'ACTIVE', created_at: '2026-10-31T10:00:00Z' }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('Merchant webhook endpoint management', () => {
  it('creates an endpoint with selected events and reveals the signing secret once in the page', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer merchant-key')
      if (url.startsWith('/v1/webhook-endpoints?')) return json({ items: [], next_cursor: null })
      if (url === '/v1/webhook-endpoints' && init?.method === 'POST') return json({ endpoint_id: 'whk_new123', signing_secret: 'whsec_one_time_value' }, 201)
      throw new Error(`Unexpected request: ${init?.method || 'GET'} ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<WebhookEndpointWorkbench token="merchant-key" onBack={vi.fn()} />)
    await screen.findByText('Endpoint ثبت‌شده‌ای وجود ندارد')
    await user.type(screen.getByLabelText('نشانی HTTPS Endpoint'), 'https://merchant.example/events')
    await user.click(screen.getByText('تحویل نهایی'))
    await user.click(screen.getByRole('button', { name: 'ثبت Endpoint' }))
    expect(await screen.findByRole('heading', { name: 'Endpoint ثبت شد' })).toBeInTheDocument()
    expect(screen.getByText('whsec_one_time_value')).toBeInTheDocument()
    expect(screen.getByText('whk_new123')).toBeInTheDocument()
    expect(localStorage.length).toBe(0)
    const post = fetchMock.mock.calls.find(([url, init]) => String(url) === '/v1/webhook-endpoints' && init?.method === 'POST')
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({ url: 'https://merchant.example/events', events: ['mission.delivered'] })
    await user.click(screen.getByRole('button', { name: 'ذخیره کردم و بستن' }))
    expect(screen.queryByText('whsec_one_time_value')).not.toBeInTheDocument()
  })

  it('requires confirmation to disable an endpoint and reflects the server state', async () => {
    let disabled = false
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.startsWith('/v1/webhook-endpoints?')) return json({ items: [{ ...endpoint, status: disabled ? 'DISABLED' : 'ACTIVE' }], next_cursor: null })
      if (url.endsWith('/disable') && init?.method === 'POST') { disabled = true; return json({ ...endpoint, status: 'DISABLED' }) }
      throw new Error(`Unexpected request: ${init?.method || 'GET'} ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<WebhookEndpointWorkbench token="merchant-key" onBack={vi.fn()} />)
    expect(await screen.findByText('whk_abc123')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'غیرفعال‌سازی' }))
    expect(screen.getByRole('dialog')).toHaveTextContent('Endpoint غیرفعال می‌شود')
    await user.click(screen.getByRole('button', { name: 'تأیید' }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/whk_abc123/disable'))).toBe(true))
    expect(await screen.findByRole('status')).toHaveTextContent('Endpoint غیرفعال شد')
    expect(await screen.findByText('غیرفعال')).toBeInTheDocument()
  })

  it('requires confirmation to rotate a signing secret and only presents the new secret once', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.startsWith('/v1/webhook-endpoints?')) return json({ items: [endpoint], next_cursor: null })
      if (url.endsWith('/rotate-secret') && init?.method === 'POST') return json({ endpoint_id: endpoint.endpoint_id, signing_secret: 'whsec_rotated_once', rotated_at: '2026-10-31T11:00:00Z' })
      throw new Error(`Unexpected request: ${init?.method || 'GET'} ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<WebhookEndpointWorkbench token="merchant-key" onBack={vi.fn()} />)
    expect(await screen.findByText('whk_abc123')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'چرخش secret' }))
    expect(screen.getByRole('dialog')).toHaveTextContent('secret قبلی باطل')
    await user.click(screen.getByRole('button', { name: 'تأیید' }))
    expect(await screen.findByRole('heading', { name: 'Secret جدید صادر شد' })).toBeInTheDocument()
    expect(screen.getByText('whsec_rotated_once')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'ذخیره کردم و بستن' }))
    expect(screen.queryByText('whsec_rotated_once')).not.toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/whk_abc123/rotate-secret'))).toBe(true)
  })

  it('reads customer-scoped lifecycle audit and renders only action and timestamp', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer merchant-key')
      if (url.startsWith('/v1/webhook-endpoints?')) return json({ items: [endpoint], next_cursor: null })
      if (url.startsWith('/v1/webhook-endpoints/whk_abc123/audit?')) return json({ items: [{ event_id: 7, action: 'SECRET_ROTATED', occurred_at: '2026-10-31T11:00:00Z' }], next_cursor: null })
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<WebhookEndpointWorkbench token="merchant-key" onBack={vi.fn()} />)
    await screen.findByText('whk_abc123')
    await user.click(screen.getByRole('button', { name: 'مشاهدهٔ سابقه' }))
    expect(await screen.findByText('سابقهٔ تغییرات')).toBeInTheDocument()
    expect(screen.getAllByText('چرخش secret')).toHaveLength(2)
    expect(screen.getAllByText(/۱۴۰۵/)).toHaveLength(2)
    expect(String(fetchMock.mock.calls[1][0])).toContain('/whk_abc123/audit?limit=50')
  })
})
