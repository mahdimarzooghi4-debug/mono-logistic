import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('courier home', () => {
  it('shows the Figma demo mission and cycles proposal cards', () => {
    render(<App />)
    expect(screen.getByText('MIS-۱۰۵۱۲')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'نمایش پیشنهاد 2' }))
    expect(screen.getByText('MIS-۱۰۵۰۹')).toBeInTheDocument()
  })

  it('toggles availability and opens the pickup route after accepting a mission', () => {
    render(<App />)
    const online = screen.getByRole('button', { name: 'آنلاین' })
    fireEvent.click(online)
    expect(screen.getByText('فعلا آفلاین هستید')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'پذیرش این ماموریت' }))
    expect(screen.getByText('MIS-۱۰۴۹۸ • حرکت به مبدا')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'گزارش مشکل' }))
    expect(screen.getByRole('status')).toHaveTextContent('گزارش مشکل برای این مأموریت ثبت شد')
    fireEvent.click(screen.getByRole('button', { name: 'رسیدم به مبدا' }))
    expect(screen.getByRole('status')).toHaveTextContent('رسیدن به مبدأ ثبت شد')
  })
})
