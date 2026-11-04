import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import CustomersDemoWorkbench from './CustomersDemoWorkbench'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('CustomersDemoWorkbench', () => {
  it('shows the Figma Hana account, branch, service and API overview without calling the backend', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    render(<CustomersDemoWorkbench />)
    expect(screen.getByRole('heading', { name: 'مشتریان سازمانی' })).toBeInTheDocument()
    expect(screen.getAllByText('سلامت API')[0].parentElement).toHaveTextContent('۹۹٫۹۸٪')
    expect(screen.getByText('ارجاع لجستیکی از حنا')).toBeInTheDocument()
    expect(screen.getByText('پیکربندی سرویس')).toBeInTheDocument()
    expect(screen.getByText('HNA-839251')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('filters linked sample orders by mission or order identifier', () => {
    render(<CustomersDemoWorkbench />)
    fireEvent.change(screen.getByRole('textbox', { name: 'جست‌وجوی سفارش، مأموریت یا موبایل' }), { target: { value: 'MIS-10461' } })
    expect(screen.getByText('HNA-839230')).toBeInTheDocument()
    expect(screen.queryByText('HNA-839251')).not.toBeInTheDocument()
  })
})
