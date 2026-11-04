import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import TrackingDemoWorkbench from './TrackingDemoWorkbench'

afterEach(()=>{cleanup();vi.unstubAllGlobals()})
describe('TrackingDemoWorkbench',()=>{
 it('shows the Figma audit metrics, immutable history and selected reassignment evidence without API calls',()=>{
  const fetchMock=vi.fn();vi.stubGlobal('fetch',fetchMock);render(<TrackingDemoWorkbench />)
  expect(screen.getByRole('heading',{name:'ردیابی و تاریخچه عملیات'})).toBeInTheDocument()
  expect(screen.getByText('۳٬۸۴۲')).toBeInTheDocument()
  expect(screen.getByText('AUD-98341')).toBeInTheDocument()
  expect(screen.getByText('assignment.v3.2')).toBeInTheDocument()
  expect(screen.getByText(/تاریخچه ردیابی قابل حذف/)).toBeInTheDocument()
  expect(fetchMock).not.toHaveBeenCalled()
 })
})
