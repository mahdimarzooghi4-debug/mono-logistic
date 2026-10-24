import { useState } from 'react'
import type { FormEvent } from 'react'

type RegistrationKind = 'business' | 'courier'

function App() {
  const [kind, setKind] = useState<RegistrationKind>('business')
  const [menuOpen, setMenuOpen] = useState(false)
  const [previewMessage, setPreviewMessage] = useState('')
  const closeMenu = () => setMenuOpen(false)
  function submitPreview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setPreviewMessage('فرم آماده است؛ ارسال درخواست پس از اتصال بک‌اند فعال می‌شود.')
  }
  return <div className="site-shell">
    <header className="site-header" id="home">
      <div className="header-inner">
        <a className="brand" href="#home" aria-label="مونو، صفحه اصلی"><span>مونو</span></a>
        <button className="menu-toggle" type="button" aria-label={menuOpen ? 'بستن منو' : 'باز کردن منو'} aria-expanded={menuOpen} aria-controls="primary-navigation" onClick={() => setMenuOpen(open => !open)}><span /><span /><span /></button>
        <nav className={menuOpen ? 'primary-navigation is-open' : 'primary-navigation'} id="primary-navigation" aria-label="ناوبری اصلی">
          <a href="#home" onClick={closeMenu}>صفحهٔ اصلی</a>
          <a href="#business" onClick={() => { setKind('business'); closeMenu() }}>برای کسب‌وکارها</a>
          <a href="#courier" onClick={() => { setKind('courier'); closeMenu() }}>برای سفیران</a>
          <a href="#about" onClick={closeMenu}>دربارهٔ مونو</a>
        </nav>
        <a className="header-cta" href="#registration" onClick={closeMenu}>شروع همکاری</a>
      </div>
    </header>
    <main>
      <section className="hero-section" aria-labelledby="hero-title">
        <div className="hero-layout">
          <div className="hero-copy">
            
            <h1 id="hero-title">مسیرها تغییر می‌کنند.<br />تعهد مونو ثابت می‌ماند.</h1>
            <p className="hero-description">پیش از پذیرش، امکان اجرای واقعی را می‌سنجیم.<br className="desktop-break" /> پس از ثبت تعهد، مسیر را تا تحویل کنترل می‌کنیم.</p>
            <a className="primary-button hero-button" href="#registration">شروع همکاری</a>
            <p className="hero-promise">مونو مالک تعهد تحویل است.</p>
          </div>
          <div className="hero-art-wrap"><img className="hero-art" src="/route-system.svg" alt="نمای مفهومی مسیرهای تحویل در هاب مونو" width="480" height="480" /></div>
        </div>
      </section>
      <section className="registration-section" id="registration" aria-labelledby="registration-title">
        <div className="registration-inner">
          <div className="registration-copy" id="about">
            <p className="eyebrow"><span /> اتصال / ۰۲</p>
            <h2 id="registration-title">به مونو<br />متصل شوید.</h2>
            <p className="registration-lead">برای شروع، چند خط کافی است.</p>
            <p className="registration-note">اطلاعات تکمیلی را در مرحلهٔ بعد می‌گیریم.</p>
          </div>
          <div className="registration-form-wrap">
            <p className="form-label">مسیر ورود</p>
            <div className="registration-tabs" role="tablist" aria-label="نوع همکاری">
              <button className={kind === 'business' ? 'registration-tab active' : 'registration-tab'} type="button" role="tab" aria-selected={kind === 'business'} id="business" onClick={() => { setKind('business'); setPreviewMessage('') }}>کسب‌وکار</button>
              <button className={kind === 'courier' ? 'registration-tab active' : 'registration-tab'} type="button" role="tab" aria-selected={kind === 'courier'} id="courier" onClick={() => { setKind('courier'); setPreviewMessage('') }}>ثبت‌نام سفیران</button>
            </div>
            <form className="registration-form" onSubmit={submitPreview} aria-label={kind === 'business' ? 'فرم همکاری کسب‌وکار' : 'فرم ثبت‌نام سفیر'}>
              <label className="field"><span>نام و نام خانوادگی</span><input name="fullName" autoComplete="name" placeholder="نام شما" required /></label>
              <label className="field"><span>شمارهٔ همراه</span><input name="phone" type="tel" inputMode="tel" autoComplete="tel" placeholder="۰۹۱۲ ۱۲۳ ۴۵۶۷" required /></label>
              {kind === 'business' && <label className="field"><span>نام کسب‌وکار</span><input name="businessName" autoComplete="organization" placeholder="مثلاً فروشگاه سپهر" required /></label>}
              <button className="primary-button submit-button" type="submit">ادامهٔ ثبت‌نام</button>
              <p className="legal-note">با ادامه، شرایط استفاده و حریم خصوصی مونو را می‌پذیرید.</p>
              <p className="form-feedback" role="status" aria-live="polite">{previewMessage}</p>
            </form>
          </div>
        </div>
      </section>
    </main>
    <footer className="site-footer">
      <div className="footer-main">
        <div className="footer-brand">
          <a className="brand footer-logo" href="#home"><span>مونو</span></a>
          <p>از امکان‌سنجی تا تعهد و تحویل؛ مسیر ارسال را با مونو روشن‌تر کنید.</p>
          <a className="footer-cta" href="#registration">شروع همکاری <span aria-hidden="true">←</span></a>
        </div>
        <div className="footer-column"><h2>همکاری با مونو</h2><a href="#business" onClick={() => setKind('business')}>برای کسب‌وکارها</a><a href="#courier" onClick={() => setKind('courier')}>برای سفیران</a></div>
        <div className="footer-column"><h2>دسترسی سریع</h2><a href="#home">صفحهٔ اصلی</a><a href="#about">دربارهٔ مونو</a><a href="#registration">فرم شروع همکاری</a></div>
        <div className="footer-contact"><h2>شروع گفتگو</h2><p>برای ثبت درخواست همکاری، فرم را تکمیل کنید.</p><a href="#registration">رفتن به فرم ثبت‌نام <span aria-hidden="true">←</span></a></div>
      </div>
      <div className="footer-bottom"><span>© {new Intl.DateTimeFormat('fa-IR-u-ca-persian', { year: 'numeric' }).format(new Date())} مونو. تمامی حقوق محفوظ است.</span><a href="#registration">شرایط استفاده و حریم خصوصی</a></div>
    </footer>
  </div>
}
export default App