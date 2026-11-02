import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import MerchantBrandingSettings from './MerchantBrandingSettings'

afterEach(cleanup)

describe('merchant brand preview', () => {
  it('updates the preview from draft values and reports demo save as local only', async () => {
    const onDemoSave = vi.fn()
    const user = userEvent.setup()
    render(<MerchantBrandingSettings mode="demo" onDemoSave={onDemoSave} />)

    const name = screen.getByLabelText('نام تجاری')
    await user.clear(name)
    await user.type(name, 'کافهٔ آبان')
    expect(screen.getAllByText('کافهٔ آبان')).toHaveLength(1)
    expect(screen.getByText('محیط نمایشی')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ذخیره تغییرات' }))
    expect(screen.getByText('تغییرهای این پیش‌نمایش در همین صفحه ثبت شدند.')).toBeInTheDocument()
    expect(onDemoSave).toHaveBeenCalledOnce()
  })

  it('keeps the connected-account save disabled without a branding API', () => {
    render(<MerchantBrandingSettings mode="connected" />)
    expect(screen.getByRole('button', { name: 'ذخیره تغییرات' })).toBeDisabled()
    expect(screen.getByText(/تنظیمات جاری از API هویت بصری دریافت نشده‌اند/)).toBeInTheDocument()
  })
})
