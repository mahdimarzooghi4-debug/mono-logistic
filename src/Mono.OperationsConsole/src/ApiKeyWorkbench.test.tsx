import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ApiKeyWorkbench from './ApiKeyWorkbench'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('API key inventory workbench', () => {
  it('loads customer credential metadata and the selected key audit without exposing secrets', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer admin-token')
      const url = String(input)
      if (url.endsWith('/audit')) return new Response(JSON.stringify({ entries: [{ keyId: 'key_1', customerId: 'cus_example', action: 'REVOKED', actorId: 'admin', reason: 'rotation', occurredAt: '2026-10-31T10:00:00Z' }] }), { status: 200 })
      return new Response(JSON.stringify({ apiKeys: [{ keyId: 'key_1', customerId: 'cus_example', scopes: ['deliveries:write'], createdAt: '2026-10-20T10:00:00Z', expiresAt: '2027-10-20T10:00:00Z', revokedAt: null, rawKey: 'do-not-render' }] }), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<ApiKeyWorkbench token="admin-token" />)
    await user.type(screen.getByLabelText('شناسهٔ مشتری'), 'cus_example')
    await user.click(screen.getByRole('button', { name: 'نمایش کلیدها' }))
    expect(await screen.findByText('key_1')).toBeInTheDocument()
    expect(screen.getByText('deliveries:write')).toBeInTheDocument()
    expect(screen.queryByText('do-not-render')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'مشاهدهٔ سابقه' }))
    expect(await screen.findByText('REVOKED')).toBeInTheDocument()
    expect(screen.getByText('admin · rotation')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    expect(String(fetchMock.mock.calls[0][0])).toContain('/customers/cus_example/api-keys')
    expect(String(fetchMock.mock.calls[1][0])).toContain('/customers/cus_example/api-keys/key_1/audit')
  })

  it('does not show data when the administrator credential is rejected', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 403 })))
    const user = userEvent.setup()
    render(<ApiKeyWorkbench token="limited-token" />)
    await user.type(screen.getByLabelText('شناسهٔ مشتری'), 'cus_private')
    await user.click(screen.getByRole('button', { name: 'نمایش کلیدها' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('دسترسی مدیر عملیات تأیید نشد')
    expect(screen.queryByText(/برای این مشتری کلیدی ثبت نشده/)).not.toBeInTheDocument()
  })
})
