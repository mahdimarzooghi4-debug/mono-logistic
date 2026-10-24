import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
afterEach(cleanup)

describe('Mono public landing', () => {
  it('renders the hero, registration choices, and footer navigation', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: /مسیرها تغییر می‌کنند/ })).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'شروع همکاری' })[0]).toHaveAttribute('href', '#registration')
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'برای سفیران' })[0]).toHaveAttribute('href', '#courier')
  })
  it('switches between business and courier forms', async () => {
    const user = userEvent.setup()
    render(<App />)
    expect(screen.getByLabelText('نام کسب‌وکار')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'ثبت‌نام سفیران' }))
    expect(screen.queryByLabelText('نام کسب‌وکار')).not.toBeInTheDocument()
    expect(screen.getByRole('form', { name: 'فرم ثبت‌نام سفیر' })).toBeInTheDocument()
  })
  it('does not claim a registration was sent before backend integration', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.type(screen.getByLabelText('نام و نام خانوادگی'), 'کاربر آزمایشی')
    await user.type(screen.getByLabelText('شمارهٔ همراه'), '09121234567')
    await user.type(screen.getByLabelText('نام کسب‌وکار'), 'فروشگاه آزمایشی')
    await user.click(screen.getByRole('button', { name: 'ادامهٔ ثبت‌نام' }))
    expect(screen.getByRole('status')).toHaveTextContent('پس از اتصال بک‌اند')
  })
})