import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import OperationsOverviewWorkbench from './OperationsOverviewWorkbench'

afterEach(cleanup)

const props = { demoMode: false, summary: null, onOpenMissions: vi.fn(), onOpenDispatch: vi.fn(), onOpenSla: vi.fn(), onOpenPresence: vi.fn(), onRefresh: vi.fn() }

describe('OperationsOverviewWorkbench', () => {
  it('matches the Figma overview sections without presenting sample values as live data', () => {
    render(<OperationsOverviewWorkbench {...props} />)
    expect(screen.getByRole('heading', { name: 'نمای کلی عملیات' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'نبض مأموریت‌ها' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'سلامت ظرفیت مناطق' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'سلامت سرویس‌ها' })).toBeInTheDocument()
    expect(screen.queryByText('MIS-۱۰۴۸۲')).not.toBeInTheDocument()
  })

  it('shows the labelled Figma sample dashboard in demo mode', () => {
    render(<OperationsOverviewWorkbench {...props} demoMode />)
    expect(screen.getByText(/داده‌های این صفحه نمایشی‌اند/)).toBeInTheDocument()
    expect(screen.getByText('MIS-۱۰۴۸۲')).toBeInTheDocument()
    expect(screen.getByText('۹۴٫۶٪')).toBeInTheDocument()
    expect(screen.getByText('۸۲٪')).toBeInTheDocument()
  })
})
