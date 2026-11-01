import { useState } from 'react'

type RecipientUnavailableProps = { onNotice: (message: string) => void }

export default function RecipientUnavailable({ onNotice }: RecipientUnavailableProps) {
  const [contactAttempts, setContactAttempts] = useState(2)
  const [registered, setRegistered] = useState(false)
  const [note, setNote] = useState('')

  function tryContact() {
    if (registered) return
    setContactAttempts(value => value + 1)
    onNotice('تلاش تماس با گیرنده ثبت شد.')
  }

  function registerUnavailable() {
    if (registered) return
    setRegistered(true)
    onNotice('عدم دسترسی گیرنده برای پشتیبانی ثبت شد.')
  }

  return <section className="content-scroll recipient-unavailable-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>گیرنده در دسترس نیست</strong><small>MIS-۱۰۴۹۸ • وضعیت تحویل</small></div>
    </header>

    <section className="unavailable-status-card">
      <div className="unavailable-status-head"><span>نیازمند اقدام</span><strong>علی رضایی</strong></div>
      <p>یوسف‌آباد، خیابان شصت‌وچهارم، پلاک ۲۱</p>
      <div className="unavailable-metrics">
        <div><small>زمان انتظار</small><b>۰۴:۳۶</b></div>
        <div><small>تماس</small><b>{new Intl.NumberFormat('fa-IR').format(contactAttempts)} بار</b></div>
        <div><small>استاندارد خدمت</small><b className="at-risk">در خطر</b></div>
      </div>
    </section>

    <section className="contact-attempts-card">
      <h1>تلاش برای تماس</h1>
      <div className="contact-attempt-row"><strong>بی‌پاسخ</strong><span>تماس تلفنی • ۱۲:۲۲</span></div>
      <div className="contact-attempt-row"><strong>بی‌پاسخ</strong><span>تماس تلفنی • ۱۲:۲۴</span></div>
      <button className="retry-contact-button" onClick={tryContact} disabled={registered}>تماس مجدد</button>
    </section>

    <section className="unavailable-guidance">
      <h2>راهنمای اقدام</h2>
      <p>در محل بمان، تماس‌ها را ثبت کن و اگر پاسخ دریافت نشد پرونده را برای بررسی پشتیبانی ثبت کن.</p>
      <small>تصمیم ادامه مسیر از طریق جریان استثنا ثبت می‌شود.</small>
    </section>

    <label className="unavailable-note">
      <strong>یادداشت — اختیاری</strong>
      <input aria-label="یادداشت عدم دسترسی" value={note} onChange={event => setNote(event.target.value)} placeholder="مثلاً: زنگ واحد و تماس تلفنی بی‌پاسخ بود." />
    </label>

    <div className="unavailable-secondary-actions">
      <button onClick={() => onNotice('درخواست پشتیبانی این مأموریت ثبت شد.')}>پشتیبانی</button>
      <button onClick={tryContact} disabled={registered}>تماس مجدد</button>
    </div>
    <button className="register-unavailable-button" onClick={registerUnavailable} disabled={registered}>
      {registered ? 'عدم دسترسی گیرنده ثبت شد' : 'ثبت عدم دسترسی گیرنده'}
    </button>
  </section>
}
