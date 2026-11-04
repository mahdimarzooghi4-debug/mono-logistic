import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ZonesCapacityWorkbench from './ZonesCapacityWorkbench'

afterEach(() => vi.unstubAllGlobals())

describe('ZonesCapacityWorkbench', () => {
  it('does not invent zone or live courier counts without an API token', () => {
    render(<ZonesCapacityWorkbench token="" onConnected={vi.fn()} />)
    expect(screen.getByText('برای نمایش فهرست واقعی، به API عملیات متصل شوید.')).toBeInTheDocument()
    expect(screen.getByText('پیک آنلاین').parentElement).toHaveTextContent('—')
    expect(screen.getByRole('button', { name: 'محاسبهٔ ظرفیت' })).toBeDisabled()
  })

  it('loads active zones from the operations catalog and selects real API data', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [{ zoneId: 'zone-central', kind: 'Micro', geometryVersion: 'v1', sourceId: 'osm', sourceRevision: 'r3', isActive: true, integrityVerified: true }], hasMore: false, nextCursor: null }) })
    vi.stubGlobal('fetch', fetchMock)
    render(<ZonesCapacityWorkbench token="ops-token" onConnected={vi.fn()} />)
    expect(await screen.findByText('zone-central')).toBeInTheDocument()
    expect(screen.getByText('یکپارچگی تأییدشده')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/operations/v1/zones/geometry-catalog?pageSize=100', expect.objectContaining({ cache: 'no-store' }))
  })

  it('shows only the empty-data state after an empty catalog response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [], hasMore: false, nextCursor: null }) }))
    render(<ZonesCapacityWorkbench token="ops-token" onConnected={vi.fn()} />)
    await waitFor(() => expect(screen.getByText('زون هندسی در کاتالوگ ثبت نشده است.')).toBeInTheDocument())
    expect(screen.queryByText('مرکزی')).not.toBeInTheDocument()
  })

  it('renders the Figma network and capacity view with labeled demo values and no API requests', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const { container } = render(<ZonesCapacityWorkbench token="" demoMode onConnected={vi.fn()} />)
    expect(screen.getByRole('heading', { name: 'منطقه مرکزی' })).toBeInTheDocument()
    expect(container.querySelector('.zones-kpis article:first-child')).toHaveTextContent('۱۲')
    expect(container.querySelector('.zones-kpis article:nth-child(3)')).toHaveTextContent('۷۱')
    expect(screen.getAllByText('غرب').length).toBeGreaterThan(0)
    expect(screen.getByRole('img', { name: 'نقشهٔ عملیاتی نمونهٔ تهران' })).toBeInTheDocument()
    expect(screen.getByText('منطقه غرب با ۴۱٪ ظرفیت و ۶ مأموریت بدون پوشش نیاز به مداخله دارد.')).toBeInTheDocument()
    expect(container.querySelector('.zone-capacity-form button')).toBeDisabled()
    expect(fetchMock).not.toHaveBeenCalled()
  })
})
