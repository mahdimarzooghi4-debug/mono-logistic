import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import IncomingCallDemoWorkbench from './IncomingCallDemoWorkbench'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('IncomingCallDemoWorkbench', () => {
  it('shows matched caller context and keeps telephony actions disabled without API calls', () => {
    const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
    render(<IncomingCallDemoWorkbench scenario="matched" onClose={vi.fn()} onOpenMission={vi.fn()} />)
    expect(screen.getByRole('heading', { name: 'تماس ورودی — زمینهٔ مأموریت' })).toBeInTheDocument()
    expect(screen.getByText('علی رضایی')).toBeInTheDocument()
    expect(screen.getByText('MIS-10479')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /پاسخ به تماس/ })).toBeDisabled()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('supports selecting one of multiple matched missions', () => {
    render(<IncomingCallDemoWorkbench scenario="multiple" onClose={vi.fn()} onOpenMission={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /MIS-10474 شعبه غرب/ }))
    expect(screen.getByText('۳ سفارش فعال')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /MIS-10474 شعبه غرب/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('offers manual search for an unmatched caller without inventing matches', () => {
    render(<IncomingCallDemoWorkbench scenario="unmatched" onClose={vi.fn()} onOpenMission={vi.fn()} />)
    fireEvent.change(screen.getByRole('textbox', { name: /جست‌وجوی تلفن/ }), { target: { value: 'MIS-10479' } })
    expect(screen.getByRole('button', { name: /MIS-10479/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'جست‌وجوی مأموریت' })).toBeDisabled()
  })
})
