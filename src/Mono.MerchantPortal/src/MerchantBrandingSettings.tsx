import { useEffect, useRef, useState } from 'react'
import type { ChangeEvent } from 'react'

type SettingsTab = 'integrations' | 'notifications' | 'members' | 'workspace' | 'branding'
type BrandValues = { brandName: string; workspaceName: string; primary: string; accent: string }

const sampleValues: BrandValues = { brandName: 'فروشگاه سپهر', workspaceName: 'ارسال و لجستیک', primary: '#7B4DFF', accent: '#A88BFF' }
const palette = ['#7B4DFF', '#00A8C6', '#D9468C', '#2166A5', '#168A52', '#B46A00']
const lockedColors = [
  { color: '#168A52', title: 'رنگ موفقیت', description: 'تحویل، وضعیت سالم و تأیید' },
  { color: '#B46A00', title: 'رنگ هشدار', description: 'ریسک، انتظار و نیازمند توجه' },
  { color: '#C83A3A', title: 'رنگ خطا', description: 'حادثه، استثنا و خطا' },
  { color: '#00A8C6', title: 'Powered by Mono', description: 'شناسهٔ زیرساخت لجستیک مونو' },
]
const tabLabels: Array<[SettingsTab, string]> = [
  ['integrations', 'یکپارچه‌سازی'], ['notifications', 'اعلان‌ها'], ['members', 'اعضا و دسترسی'], ['workspace', 'Workspace'], ['branding', 'هویت بصری'],
]

export default function MerchantBrandingSettings({
  mode,
  onTabChange,
  onDemoSave,
}: {
  mode: 'demo' | 'connected'
  onTabChange?: (tab: SettingsTab) => void
  onDemoSave?: () => void
}) {
  const isDemo = mode === 'demo'
  const [values, setValues] = useState<BrandValues>(isDemo ? sampleValues : { brandName: '', workspaceName: '', primary: sampleValues.primary, accent: sampleValues.accent })
  const [logoUrl, setLogoUrl] = useState<string | null>(null)
  const [logoError, setLogoError] = useState('')
  const [savedPreview, setSavedPreview] = useState(false)
  const logoUrlRef = useRef<string | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => () => {
    if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current)
  }, [])

  function setValue<K extends keyof BrandValues>(key: K, value: BrandValues[K]) {
    setValues((current) => ({ ...current, [key]: value }))
    setSavedPreview(false)
  }

  function resetDraft() {
    setValues(isDemo ? sampleValues : { brandName: '', workspaceName: '', primary: sampleValues.primary, accent: sampleValues.accent })
    setLogoError('')
    setSavedPreview(false)
    if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current)
    logoUrlRef.current = null
    setLogoUrl(null)
    if (fileRef.current) fileRef.current.value = ''
  }

  function chooseLogo(event: ChangeEvent<HTMLInputElement>) {
    setLogoError('')
    const file = event.currentTarget.files?.[0]
    if (!file) return
    const allowed = ['image/png', 'image/jpeg', 'image/svg+xml']
    if (!allowed.includes(file.type)) {
      setLogoError('فقط فایل PNG، JPG یا SVG انتخاب کنید.')
      event.currentTarget.value = ''
      return
    }
    if (file.size > 2 * 1024 * 1024) {
      setLogoError('حجم فایل باید کمتر از ۲ مگابایت باشد.')
      event.currentTarget.value = ''
      return
    }
    if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current)
    logoUrlRef.current = URL.createObjectURL(file)
    setLogoUrl(logoUrlRef.current)
    setSavedPreview(false)
  }

  function updateHex(key: 'primary' | 'accent', raw: string) {
    const normalized = raw.trim().toUpperCase()
    if (/^#[0-9A-F]{6}$/.test(normalized)) setValue(key, normalized)
  }

  return <section className="merchant-branding-page" dir="rtl">
    {isDemo && <div className="demo-ribbon"><span>محیط نمایشی</span><b>همهٔ تغییرها موقت‌اند و به Workspace واقعی ذخیره یا ارسال نمی‌شوند.</b></div>}
    <header className="branding-page-header">
      <div><span className="eyebrow">هویت Workspace</span><h1>تنظیمات هویت بصری</h1><p>ظاهر Workspace خودت را با برند کسب‌وکارت هماهنگ کن.</p></div>
      <div className="branding-header-actions"><button className="secondary-button" onClick={resetDraft}>بازنشانی</button><button className="primary-button" disabled={!isDemo || savedPreview} title={!isDemo ? 'ذخیره‌سازی هویت بصری هنوز در API حساب موجود نیست.' : undefined} onClick={() => { setSavedPreview(true); onDemoSave?.() }}>ذخیره تغییرات</button></div>
    </header>
    <nav className="settings-tabs branding-tabs" aria-label="بخش‌های تنظیمات Workspace" role="tablist">
      {tabLabels.map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={id === 'branding'} className={id === 'branding' ? 'selected' : ''} disabled={id !== 'branding' && (!onTabChange || isDemo)} onClick={() => { if (id !== 'branding') onTabChange?.(id) }}>{label}</button>)}
    </nav>
    {!isDemo && <p className="branding-capability-note">تنظیمات جاری از API هویت بصری دریافت نشده‌اند. مقادیر زیر فقط پیش‌نمایش موقت مرورگرند و دکمهٔ ذخیره تا آماده‌شدن API غیرفعال است.</p>}
    {savedPreview && isDemo && <p className="branding-preview-saved" role="status">تغییرهای این پیش‌نمایش در همین صفحه ثبت شدند.</p>}
    <div className="branding-layout">
      <aside className="branding-live-preview">
        <div className="branding-preview-title"><span className="branding-preview-pill">Preview</span><h2>پیش‌نمایش زنده</h2></div>
        <div className="branding-mock-workspace" style={{ '--brand-primary': values.primary, '--brand-accent': values.accent } as React.CSSProperties}>
          <aside className="branding-mock-sidebar">
            <div className="branding-mock-identity"><div className="branding-mock-logo">{logoUrl ? <img src={logoUrl} alt="لوگوی انتخاب‌شده" /> : <span>{(values.brandName || 'س').trim().slice(0, 1)}</span>}</div><strong>{values.brandName || 'نام کسب‌وکار'}</strong><small>Powered by Mono</small></div>
            <nav aria-label="پیش‌نمایش منو"><span>داشبورد</span><span>ارسال جدید</span><span>ارسال‌ها</span><span>رهگیری</span><span className="current">تنظیمات</span></nav>
          </aside>
          <div className="branding-mock-content"><h3>داشبورد کسب‌وکار</h3><small>{values.workspaceName || 'عنوان Workspace'}</small><button type="button">ارسال جدید</button><article><strong>ارسال فعال</strong><div><span>در مسیر</span><span>تحویل‌شده</span><span>هشدار</span></div></article><article className="branding-mock-rows"><i /><i /><i /><i /></article></div>
        </div>
        <div className="branding-preview-footnote"><span>تغییرات فقط در پیش‌نمایش دیده می‌شوند.</span><strong>{isDemo ? 'ذخیره، دادهٔ واقعی را تغییر نمی‌دهد.' : 'پیش‌نمایش محلی؛ تنظیمات واقعی از API نیامده‌اند.'}</strong></div>
      </aside>
      <div className="branding-form-column">
        <section className="branding-settings-card">
          <div className="branding-card-heading"><div><h2>هویت برند</h2><p>این اطلاعات در Header و Sidebar پنل Workspace نمایش داده می‌شوند.</p></div></div>
          <div className="branding-identity-grid">
            <div className="branding-logo-control"><div className="branding-logo-preview" style={{ '--brand-primary': values.primary, '--brand-accent': values.accent } as React.CSSProperties}>{logoUrl ? <img src={logoUrl} alt="لوگوی انتخاب‌شده" /> : <span>{(values.brandName || 'س').trim().slice(0, 1)}</span>}</div><div className="branding-logo-actions"><strong>لوگوی کسب‌وکار</strong><small>PNG، JPG یا SVG · حداکثر ۲ مگابایت</small><div><button className="secondary-button" type="button" onClick={() => fileRef.current?.click()}>جایگزینی لوگو</button><button className="danger-outline-button" type="button" disabled={!logoUrl} onClick={() => { if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current); logoUrlRef.current = null; setLogoUrl(null); if (fileRef.current) fileRef.current.value = '' }}>حذف لوگو</button></div><input ref={fileRef} className="sr-only" type="file" accept="image/png,image/jpeg,image/svg+xml" onChange={chooseLogo} aria-label="انتخاب لوگوی کسب‌وکار" />{logoError && <small className="branding-error" role="alert">{logoError}</small>}</div></div>
            <label className="branding-field">نام تجاری<input value={values.brandName} maxLength={80} onChange={(event) => setValue('brandName', event.target.value)} placeholder={isDemo ? 'فروشگاه سپهر' : 'هنوز از API دریافت نشده'} /></label>
            <label className="branding-field">عنوان Workspace<input value={values.workspaceName} maxLength={100} onChange={(event) => setValue('workspaceName', event.target.value)} placeholder={isDemo ? 'ارسال و لجستیک' : 'هنوز از API دریافت نشده'} /></label>
          </div>
        </section>
        <section className="branding-settings-card">
          <div className="branding-card-heading"><div><h2>رنگ‌های برند</h2><p>رنگ‌های اصلی و تأکیدی روی پوسته و اجزای تعاملی اثر می‌گذارند.</p></div></div>
          <div className="branding-color-grid">
            <label className="branding-color-field"><span>رنگ اصلی</span><div><input type="color" value={values.primary} onChange={(event) => setValue('primary', event.target.value.toUpperCase())} aria-label="انتخاب رنگ اصلی" /><input className="branding-hex-input" value={values.primary} maxLength={7} onChange={(event) => updateHex('primary', event.target.value)} aria-label="کد رنگ اصلی" /></div></label>
            <label className="branding-color-field"><span>رنگ Accent</span><div><input type="color" value={values.accent} onChange={(event) => setValue('accent', event.target.value.toUpperCase())} aria-label="انتخاب رنگ Accent" /><input className="branding-hex-input" value={values.accent} maxLength={7} onChange={(event) => updateHex('accent', event.target.value)} aria-label="کد رنگ Accent" /></div></label>
          </div>
          <div className="branding-palette" aria-label="رنگ‌های پیشنهادی">{palette.map((color) => <button key={color} type="button" style={{ backgroundColor: color }} aria-label={`انتخاب رنگ ${color}`} aria-pressed={values.primary === color} onClick={() => setValue('primary', color)} />)}</div>
          <small className="branding-form-hint">برای واردکردن کد، قالب HEX شش‌رقمی مانند ‎#7B4DFF را بنویسید.</small>
        </section>
        <section className="branding-settings-card branding-locked-card">
          <div className="branding-card-heading"><div><h2>موارد غیرقابل تغییر</h2><p>برای حفظ معنا و یکسان‌بودن وضعیت‌ها، این رنگ‌ها ثابت هستند.</p></div><span className="branding-lock-badge">قفل</span></div>
          <div className="branding-locked-list">{lockedColors.map((item) => <div key={item.title}><i style={{ backgroundColor: item.color }} /><div><strong>{item.title}</strong><small>{item.description}</small></div><span>قفل</span></div>)}</div>
        </section>
      </div>
    </div>
  </section>
}
