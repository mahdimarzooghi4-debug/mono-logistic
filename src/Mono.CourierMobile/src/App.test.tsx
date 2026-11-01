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

  it('toggles availability and accepts an offer in demo mode', () => {
    render(<App />)
    const online = screen.getByRole('button', { name: 'آنلاین' })
    fireEvent.click(online)
    expect(screen.getByText('فعلا آفلاین هستید')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'پذیرش این ماموریت' }))
    expect(screen.getByRole('status')).toHaveTextContent('MIS-۱۰۵۱۲')
  })
})
