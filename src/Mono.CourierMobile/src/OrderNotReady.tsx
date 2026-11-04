import { useEffect, useState } from 'react'

type OrderNotReadyProps = { onNotice: (message: string) => void }

function toPersianDigits(value: string) {
  return value.replace(/[0-9]/g, digit => '۰۱۲۳۴۵۶۷۸۹'[Number(digit)])
}

export default function OrderNotReady({ onNotice }: OrderNotReadyProps) {
  const [waitSeconds, setWaitSeconds] = useState(312)
  const [note, setNote] = useState('')
  const [registered, setRegistered] = useState(false)

  useEffect(() => {
    const timer = window.setInterval(() => setWaitSeconds(seconds => seconds + 1), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const wait = `${toPersianDigits(String(Math.floor(waitSeconds / 60)).padStart(2, '0'))}:${toPersianDigits(String(waitSeconds % 60).padStart(2, '0'))}`

  function registerDelay() {
    if (registered) return
    setRegistered(true)
    onNotice('تأخیر آماده‌سازی فروشگاه ثبت شد؛ به عملکرد پیک منظور نمی‌شود.')
  }

  return <section className="content-scroll order-not-ready-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>سفارش آماده نیست</strong><small>MIS-۱۰۴۹۸ • انتظار در مبدا</small></div>
    </header>

    <section className="store-delay-status-card">
      <div className="store-delay-head"><span>{registered ? 'ثبت شد' : 'در انتظار'}</span><strong>شعبه مرکزی حنا</strong></div>
      <p>سفارش هنوز از طرف شعبه آماده تحویل نشده است.</p>
      <div className="store-delay-metrics">
        <div><small>زمان انتظار</small><b>{wait}</b></div>
        <div><small>حد مجاز</small><b>۳ دقیقه</b></div>
        <div><small>وضعیت</small><b className="delay-risk">{registered ? 'تأخیر ثبت شد' : 'ثبت تأخیر'}</b></div>
      </div>
    </section>

    <section className="delay-policy-card">
      <h1>تأخیر آماده‌سازی فروشگاه</h1>
      <p>انتظار از ۳ دقیقه عبور کرده است. این زمان به‌عنوان تأخیر آماده‌سازی فروشگاه ثبت می‌شود و به حساب عملکرد پیک منظور نمی‌شود.</p>
      <small>زمان انتظار ادامه دارد تا سفارش واقعاً تحویل پیک شود.</small>
    </section>

    <section className="delay-order-details">
      <h2>اطلاعات سفارش</h2>
      <div className="account-data-row"><strong className="account-teal">HNA-۸۳۹۲۵۱</strong><span>شماره سفارش</span></div>
      <div className="account-data-row"><strong>۲</strong><span>تعداد بسته</span></div>
      <div className="account-data-row"><strong>۱۲:۱۰</strong><span>زمان رسیدن پیک</span></div>
      <div className="account-data-row"><strong className="delay-warning">{registered ? 'تأخیر ثبت شد' : 'آماده‌سازی ادامه دارد'}</strong><span>وضعیت شعبه</span></div>
    </section>

    <label className="delay-note-card">
      <strong>یادداشت — اختیاری</strong>
      <input aria-label="یادداشت تأخیر آماده‌سازی" value={note} onChange={event => setNote(event.target.value)} placeholder="مثلاً: مسئول شعبه اعلام کرد سفارش ۵ دقیقه دیگر آماده می‌شود." />
    </label>

    <div className="delay-actions">
      <button onClick={() => onNotice('تماس با شعبه حنا در نسخهٔ نمایشی فعال نیست.')}>تماس با شعبه</button>
      <button onClick={() => onNotice('درخواست پشتیبانی درباره تأخیر سفارش ثبت شد.')}>پشتیبانی</button>
    </div>
    <button className="register-delay-button" onClick={registerDelay} disabled={registered}>
      {registered ? 'تأخیر شعبه ثبت شد' : 'ثبت تأخیر شعبه'}
    </button>
  </section>
}
