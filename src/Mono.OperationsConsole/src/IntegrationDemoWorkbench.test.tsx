import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import IntegrationDemoWorkbench from './IntegrationDemoWorkbench'

afterEach(()=>{cleanup();vi.unstubAllGlobals()})
describe('IntegrationDemoWorkbench',()=>{
 it('shows the Figma Hana API, webhook health, security contract and recent activity without API calls',()=>{
  const fetchMock=vi.fn();vi.stubGlobal('fetch',fetchMock);render(<IntegrationDemoWorkbench />)
  expect(screen.getByRole('heading',{name:'یکپارچه‌سازی'})).toBeInTheDocument()
  expect(screen.getByText(/hana-prod-۰۱/)).toBeInTheDocument()
  expect(screen.getByText('OAuth۲ Client Credentials')).toBeInTheDocument()
  expect(screen.getAllByText('mission.sla_at_risk').length).toBeGreaterThan(0)
  expect(fetchMock).not.toHaveBeenCalled()
 })
})
