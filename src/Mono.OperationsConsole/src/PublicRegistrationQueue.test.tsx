import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import PublicRegistrationQueue from './PublicRegistrationQueue'

describe('PublicRegistrationQueue', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('loads decrypted applications and records an operator contact attempt', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/transition')) return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(new Response(JSON.stringify([{
        requestId: 'reg_0123456789abcdef0123456789abcdef', type: 'BUSINESS', status: 'SUBMITTED',
        fullName: 'کاربر نمونه', phone: '09121234567', businessName: 'فروشگاه نمونه',
        privacyNoticeVersion: 'v1', submittedAt: '2026-10-24T10:00:00Z',
      }]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<PublicRegistrationQueue token="staff-token" />)
    expect(await screen.findByText('کاربر نمونه')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'ثبت تماس' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/operations/v1/public-registration-requests/reg_0123456789abcdef0123456789abcdef/transition',
      expect.objectContaining({ method: 'POST', headers: expect.objectContaining({ Authorization: 'Bearer staff-token' }) }),
    ))
    expect(await screen.findByRole('status')).toHaveTextContent('وضعیت درخواست به‌روزرسانی شد.')
  })
})
