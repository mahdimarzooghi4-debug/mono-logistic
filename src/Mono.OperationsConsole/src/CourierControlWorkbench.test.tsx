import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import CourierControlWorkbench from './CourierControlWorkbench'

const courier = {
  courier_id: 'crr_test_01', mode: 'BIKE', status: 'ACTIVE', preferred_zone_ids: ['north'],
  approved_zone_ids: ['central'], primary_zone_id: 'central', created_at: '2026-10-01T10:00:00Z', updated_at: '2026-10-31T10:00:00Z',
}

describe('Courier control workspace', () => {
  it('uses the selected courier record and keeps unsupported GPS and operations explicit', () => {
    render(<CourierControlWorkbench
      couriers={[courier]} selectedCourierId={courier.courier_id}
      detail={{ ...courier, created_by: 'ops-admin', trust_transitions: [] }} audit={[]}
      loading={false} detailLoading={false} detailErrors={[]} error="" query="" status="" token="verified-admin"
      pageIndex={0} hasNext={false} onQueryChange={vi.fn()} onStatusChange={vi.fn()} onSearch={vi.fn()}
      onSelect={vi.fn()} onPrevious={vi.fn()} onNext={vi.fn()} onOpenAdmissions={vi.fn()} onOpenZones={vi.fn()} onRefresh={vi.fn()}
    />)

    expect(screen.getByRole('heading', { name: courier.courier_id })).toBeInTheDocument()
    expect(screen.getByText('موتورسیکلت')).toBeInTheDocument()
    expect(screen.getByText('موقعیت زنده در API پیک موجود نیست')).toBeInTheDocument()
    expect(screen.getByText('پیک‌های آنلاین').parentElement).toHaveTextContent('—')
    expect(screen.getByText('باتری دستگاه')).toBeInTheDocument()
    expect(screen.getAllByText('مأموریت فعال')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'تماس با پیک' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'محدودسازی موقت' })).toBeDisabled()
  })
})
