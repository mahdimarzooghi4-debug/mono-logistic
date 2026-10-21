import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import PolicyWorkbench from './PolicyWorkbench'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const policy = {
  policyId: `pol_${'a'.repeat(32)}`,
  policyType: 'COVERAGE_ZONE',
  scope: 'CUSTOMER',
  scopeId: 'cus_test',
  createdAt: '2026-10-21T10:00:00Z',
  createdBy: 'mono-admin-test',
  latestVersion: 1,
  latestStatus: 'DRAFT',
}

describe('Operations policy workbench', () => {
  it('loads the API registry and shows policy detail and append-only audit with the operations token', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (!init) throw new Error('Expected request options')
      const url = String(input)
      if (url === '/operations/v1/policies?page_size=50') {
        return new Response(JSON.stringify({ policies: [policy], nextCursor: null }), { status: 200 })
      }
      if (url === `/operations/v1/policies/${policy.policyId}`) {
        return new Response(JSON.stringify({ ...policy, versions: [{
          policyId: policy.policyId, versionNumber: 1, enforcementType: 'HARD', priority: 10,
          status: 'DRAFT', effectiveFrom: null, effectiveTo: null,
          definition: { constraints: [{ fact: 'distance_km', operator: 'LTE', value: 5, reasonCode: 'MAX_DISTANCE' }] },
          createdAt: policy.createdAt, createdBy: policy.createdBy,
        }] }), { status: 200 })
      }
      if (url === `/operations/v1/policies/${policy.policyId}/audit`) {
        return new Response(JSON.stringify({ entries: [{
          auditId: 'audit_test', versionNumber: 1, action: 'POLICY_CREATED', actorId: policy.createdBy,
          reason: 'initial review', occurredAt: policy.createdAt,
        }] }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<PolicyWorkbench token="verified-admin-token" reloadKey={0} onConnected={vi.fn()} />)

    expect(await screen.findByText(policy.policyId)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'بازکردن' }))
    expect(await screen.findByRole('heading', { name: policy.policyId })).toBeInTheDocument()
    expect(screen.getByText('POLICY_CREATED')).toBeInTheDocument()
    expect(screen.getByText(/به درخواست تحویل یا مأموریت اعمال نمی‌شود/)).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
    for (const call of fetchMock.mock.calls) {
      expect(new Headers(call[1]?.headers).get('Authorization')).toBe('Bearer verified-admin-token')
    }
  })

  it('surfaces the backend MFA authorization gate instead of showing sample policy data', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ error: { code: 'FORBIDDEN' } }), { status: 403 })))
    render(<PolicyWorkbench token="admin-without-mfa" reloadKey={0} onConnected={vi.fn()} />)

    expect(await screen.findByRole('alert')).toHaveTextContent('دسترسی مدیر Mono با MFA تأیید نشد')
    expect(screen.queryByText(policy.policyId)).not.toBeInTheDocument()
  })

  it('requires an auditable reason and a second explicit confirmation before lifecycle mutation', async () => {
    const version = {
      policyId: policy.policyId, versionNumber: 1, enforcementType: 'HARD', priority: 10,
      status: 'DRAFT', effectiveFrom: null, effectiveTo: null, definition: { constraints: [] },
      createdAt: policy.createdAt, createdBy: policy.createdBy,
    }
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url === '/operations/v1/policies?page_size=50')
        return new Response(JSON.stringify({ policies: [policy], nextCursor: null }), { status: 200 })
      if (url === `/operations/v1/policies/${policy.policyId}`)
        return new Response(JSON.stringify({ policyId: policy.policyId, policyType: policy.policyType, scope: policy.scope, scopeId: policy.scopeId, createdAt: policy.createdAt, createdBy: policy.createdBy, versions: [version] }), { status: 200 })
      if (url === `/operations/v1/policies/${policy.policyId}/audit`)
        return new Response(JSON.stringify({ entries: [] }), { status: 200 })
      if (url.endsWith('/versions/1/activate') && init?.method === 'POST')
        return new Response(JSON.stringify({ ...version, status: 'ACTIVE' }), { status: 200 })
      throw new Error(`Unexpected request: ${init?.method || 'GET'} ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<PolicyWorkbench token="verified-admin-token" reloadKey={0} onConnected={vi.fn()} />)
    await user.click(await screen.findByRole('button', { name: 'بازکردن' }))
    await user.type(await screen.findByLabelText(/دلیل فعال‌سازی یا بازنشست/), 'approved by operations')
    await user.click(screen.getByRole('button', { name: 'فعال‌سازی' }))
    expect(fetchMock.mock.calls.some(([url, init]) => String(url).includes('/activate') && init?.method === 'POST')).toBe(false)
    await user.click(screen.getByRole('button', { name: 'تأیید' }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url).includes('/activate') && init?.method === 'POST')).toBe(true))
    const action = fetchMock.mock.calls.find(([url, init]) => String(url).includes('/activate') && init?.method === 'POST')
    expect(JSON.parse(String(action?.[1]?.body))).toEqual({ reason: 'approved by operations' })
  })

  it('sends HARD evaluation as a preview request and displays the authoritative result', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input) === '/operations/v1/policies?page_size=50')
        return new Response(JSON.stringify({ policies: [], nextCursor: null }), { status: 200 })
      if (String(input) === '/operations/v1/policies/evaluate' && init?.method === 'POST')
        return new Response(JSON.stringify({ requestedAt: '2026-10-21T10:00:00Z', evaluatedHardPolicyCount: 1, ignoredNonHardPolicyCount: 0, passed: false, candidateExcluded: true, reasonCodes: ['MAX_DISTANCE'], policies: [] }), { status: 200 })
      throw new Error(`Unexpected request: ${init?.method || 'GET'} ${String(input)}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<PolicyWorkbench token="verified-admin-token" reloadKey={0} onConnected={vi.fn()} />)
    await screen.findByText('سیاستی در این صفحه از API برنگشت.')
    await userEvent.click(screen.getByRole('button', { name: 'ارزیابی از API' }))

    expect(await screen.findByText('نامزد در این پیش‌نمایش کنار گذاشته می‌شود.')).toBeInTheDocument()
    const evaluationCall = fetchMock.mock.calls.find(([url]) => String(url) === '/operations/v1/policies/evaluate')
    expect(evaluationCall?.[1]?.method).toBe('POST')
    expect(JSON.parse(String(evaluationCall?.[1]?.body))).toMatchObject({
      serviceId: 'urban-delivery',
      facts: { distance_km: 4.5, mode: 'BIKE' },
    })
  })
})
