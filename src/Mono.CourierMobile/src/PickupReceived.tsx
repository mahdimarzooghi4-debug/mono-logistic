import { useEffect, useState } from 'react'

type PickupReceivedProps = { onNotice: (message: string) => void; onHandedOff: () => void }

export default function PickupReceived({ onNotice, onHandedOff }: PickupReceivedProps) {
  const [waitSeconds, setWaitSeconds] = useState(134)
  const [countChecked, setCountChecked] = useState(false)
  const [packageChecked, setPackageChecked] = useState(false)
  const [handedOff, setHandedOff] = useState(false)

  useEffect(() => {
    const timer = window.setInterval(() => setWaitSeconds(seconds => seconds + 1), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const wait = `${String(Math.floor(waitSeconds / 60)).padStart(2, '0')}:${String(waitSeconds % 60).padStart(2, '0')}`

  function confirmHandoff() {
    if (!countChecked || !packageChecked) {
      onNotice('برای تأیید تحویل، تعداد و سلامت بسته‌ها را بررسی کنید.')
      return
    }
    setHandedOff(true)
    onHandedOff()
    onNotice('تحویل سفارش از شعبه ثبت شد.')
  }

  return <section className="content-scroll received-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>دریافت سفارش</strong><small>MIS-۱۰۴۹۸ • رسیدن به مبدا</small></div>
    </header>

    <section className="received-status-card">
      <div className="received-status-head"><span className="arrived-pill">رسیده‌ام</span><strong>شعبه مرکزی حنا</strong></div>
      <p className="received-address">خیابان ولیعصر، بالاتر از میدان ونک</p>
      <div className="received-metrics">
        <div><small>زمان انتظار</small><b>{wait}</b></div>
        <div><small>حد مجاز</small><b>۳ دقیقه</b></div>
        <div><small>آمادگی</small><b className="ready">آماده</b></div>
      </div>
    </section>

    <section className="waiting-card">
      <div className="received-card-heading"><span className="waiting-pill">در انتظار تحویل</span><strong>زمان دریافت ثبت می‌شود</strong></div>
      <p>اگر آماده‌سازی شعبه بیشتر از ۳ دقیقه شود، تأخیر به‌عنوان «تأخیر آماده‌سازی فروشگاه» ثبت می‌شود و به حساب پیک منظور نمی‌شود.</p>
    </section>

    <section className="package-checklist">
      <div className="received-card-heading"><span className="package-pill">۲ بسته</span><strong>کنترل هنگام تحویل</strong></div>
      <div className="package-row"><span>HNA-۸۳۹۲۵۱</span><small>شماره سفارش</small></div>
      <div className="package-row"><span>۲</span><small>تعداد کیسه</small></div>
      <div className="package-row"><span>متوسط</span><small>بار تقریبی</small></div>
      <div className="package-row"><span>عمودی • مراقبت از مایعات</span><small>شرایط حمل</small></div>
      <div className="pickup-checks">
        <label className={countChecked ? 'checked' : ''}><input type="checkbox" checked={countChecked} onChange={event => setCountChecked(event.target.checked)} /><span>تعداد درست است</span></label>
        <label className={packageChecked ? 'checked' : ''}><input type="checkbox" checked={packageChecked} onChange={event => setPackageChecked(event.target.checked)} /><span>بسته سالم است</span></label>
      </div>
    </section>

    <div className="pickup-problem-actions">
      <button onClick={() => onNotice('وضعیت «سفارش آماده نیست» برای این مأموریت ثبت شد.')}>سفارش آماده نیست</button>
      <button onClick={() => onNotice('فرم ثبت مشکل تحویل برای این مأموریت باز شد.')}>مشکل در تحویل</button>
    </div>

    <section className="handoff-card">
      <strong>تأیید تحویل از شعبه</strong>
      <p>پس از تحویل فیزیکی بسته‌ها، این مرحله را ثبت کن. از این لحظه سفارش در اختیار پیک است.</p>
      <button onClick={confirmHandoff} disabled={handedOff}>{handedOff ? 'تحویل ثبت شد' : 'سفارش را تحویل گرفتم'}</button>
    </section>
  </section>
}
