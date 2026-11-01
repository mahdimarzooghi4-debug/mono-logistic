import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import VehiclesDemoWorkbench from './VehiclesDemoWorkbench'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('VehiclesDemoWorkbench', () => {
  it('shows the Figma vehicle overview, selected vehicle eligibility and documents without API calls', () => {
    const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
    render(<VehiclesDemoWorkbench />)
    expect(screen.getByRole('heading', { name: 'وسایل نقلیه' })).toBeInTheDocument()
    expect(screen.getByText('۷۲')).toBeInTheDocument()
    expect(screen.getByText(/واجد شرایط مأموریت جدید/)).toBeInTheDocument()
    expect(screen.getByText('کارت وسیله')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('filters the vehicle list by type and identifier', () => {
    render(<VehiclesDemoWorkbench />)
    fireEvent.click(screen.getByRole('button', { name: 'خودرو' }))
    expect(screen.getByText('VEH-302')).toBeInTheDocument()
    expect(screen.queryByText('VEH-118')).not.toBeInTheDocument()
  })
})
