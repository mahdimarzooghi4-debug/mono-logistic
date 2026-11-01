import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import SupportCasesDemoWorkbench from './SupportCasesDemoWorkbench'

afterEach(()=>{cleanup();vi.unstubAllGlobals()})
describe('SupportCasesDemoWorkbench',()=>{
 it('shows Figma referral queue, ownership evidence and recommended response without API calls',()=>{
  const fetchMock=vi.fn();vi.stubGlobal('fetch',fetchMock);render(<SupportCasesDemoWorkbench />)
  expect(screen.getByRole('heading',{name:'CAS-02841 — تأخیر در دریافت'})).toBeInTheDocument()
  expect(screen.getByText('درخواست باز').parentElement).toHaveTextContent('۱۷')
  expect(screen.getByText('STORE_PREPARATION_DELAY')).toBeInTheDocument()
  expect(screen.getByRole('button',{name:'ارسال پاسخ به حنا'})).toBeDisabled()
  expect(fetchMock).not.toHaveBeenCalled()
 })
 it('filters the referral queue by severity',()=>{
  render(<SupportCasesDemoWorkbench />)
  fireEvent.click(screen.getByRole('button',{name:'بحرانی'}))
  expect(screen.getByText('CAS-02810')).toBeInTheDocument()
  expect(screen.queryByText('CAS-02836')).not.toBeInTheDocument()
 })
})
