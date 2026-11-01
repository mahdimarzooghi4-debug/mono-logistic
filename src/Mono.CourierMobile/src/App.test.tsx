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

  it('moves through pickup and requires package checks before handoff', () => {
    render(<App />)
    const online = screen.getByRole('button', { name: 'آنلاین' })
    fireEvent.click(online)
    expect(screen.getByText('فعلا آفلاین هستید')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'پذیرش این ماموریت' }))
    expect(screen.getByText('MIS-۱۰۴۹۸ • حرکت به مبدا')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'گزارش مشکل' }))
    expect(screen.getByRole('status')).toHaveTextContent('گزارش مشکل برای این مأموریت ثبت شد')
    fireEvent.click(screen.getByRole('button', { name: 'رسیدم به مبدا' }))
    expect(screen.getByText('MIS-۱۰۴۹۸ • رسیدن به مبدا')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'سفارش را تحویل گرفتم' }))
    expect(screen.getByRole('status')).toHaveTextContent('تعداد و سلامت بسته‌ها را بررسی کنید')
    fireEvent.click(screen.getByLabelText('تعداد درست است'))
    fireEvent.click(screen.getByLabelText('بسته سالم است'))
    fireEvent.click(screen.getByRole('button', { name: 'سفارش را تحویل گرفتم' }))
    expect(screen.getByText('MIS-۱۰۴۹۸ • حرکت به مقصد')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'رسیدم به مقصد' }))
    expect(screen.getByText('MIS-۱۰۴۹۸ • رسیدن به مقصد')).toBeInTheDocument()
    expect(screen.getByLabelText('یادداشت تحویل اختیاری')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'تحویل انجام شد' }))
    expect(screen.getByRole('status')).toHaveTextContent('تحویل سفارش ثبت شد')
    expect(screen.getByRole('button', { name: 'تحویل ثبت شد' })).toBeDisabled()
  })
})
