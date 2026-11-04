import { useState } from 'react'

type ReturnHandoffProps = { onNotice: (message: string) => void; onComplete: () => void }
type ReturnCondition = 'all' | 'shortage'

export default function ReturnHandoff({ onNotice, onComplete }: ReturnHandoffProps) {
  const [condition, setCondition] = useState<ReturnCondition>('all')
  const [note, setNote] = useState('')
  const [submitted, setSubmitted] = useState(false)

  function submitHandoff() {
    if (submitted) return
    if (condition === 'shortage' && !note.trim()) {
      onNotice('برای ثبت کسری، یادداشت کوتاهی وارد کن.')
      return
    }
    setSubmitted(true)
    onComplete()
  }

  return <section className="content-scroll return-handoff-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>تحویل برگشتی</strong><small>MIS-۱۰۴۹۸ • رسیدن به مبدا</small></div>
    </header>

    <section className="return-arrival-card">
      <div className="return-arrival-head"><span>رسیده‌ام</span><strong>شعبه مرکزی حنا</strong></div>
      <p>بازگشت طبق تصمیم پشتیبانی • پرونده INC-۰۳۱۸</p>
      <div className="return-arrival-metrics">
        <div><small>بسته</small><strong>۲</strong></div>
        <div><small>وضعیت</small><strong className="return-status-waiting">نزد پیک</strong></div>
        <div><small>مرحله</small><strong className="return-status-teal">تحویل برگشت</strong></div>
      </div>
    </section>

    <section className="return-handoff-card">
      <h1>تأیید تحویل به شعبه</h1>
      <div className="return-handoff-row"><strong className="return-status-teal">HNA-۸۳۹۲۵۱</strong><span>سفارش</span></div>
      <div className="return-handoff-row"><strong>۲</strong><span>تعداد بسته برگشتی</span></div>
      <div className="return-handoff-row"><strong>مهدی کاظمی</strong><span>تحویل‌گیرنده شعبه</span></div>
      <div className="return-condition-options" role="radiogroup" aria-label="وضعیت تحویل بسته‌های برگشتی">
        <label className={condition === 'all' ? 'selected' : ''}>
          <input type="radio" name="return-condition" checked={condition === 'all'} onChange={() => setCondition('all')} />
          <img src={condition === 'all' ? '/figma/return-handoff-selected.svg' : '/figma/return-handoff-unselected.svg'} alt="" />
          <span>هر دو بسته تحویل شد</span>
        </label>
        <label className={condition === 'shortage' ? 'selected' : ''}>
          <input type="radio" name="return-condition" checked={condition === 'shortage'} onChange={() => setCondition('shortage')} />
          <img src={condition === 'shortage' ? '/figma/return-handoff-selected.svg' : '/figma/return-handoff-unselected.svg'} alt="" />
          <span>کسری در برگشت</span>
        </label>
      </div>
    </section>

    <section className="return-package-status-card">
      <h2>وضعیت بسته هنگام برگشت</h2>
      <div className="return-handoff-row"><strong className="return-package-damage">آسیب ظاهری ثبت‌شده</strong><span>بسته ۱</span></div>
      <div className="return-handoff-row"><strong className="return-package-good">سالم</strong><span>بسته ۲</span></div>
      <div className="return-handoff-row"><strong className="return-package-pending">باز تا بررسی نهایی</strong><span>پرونده حادثه</span></div>
    </section>

    <label className="return-handoff-note">
      <strong>یادداشت برگشت — اختیاری</strong>
      <input aria-label="یادداشت برگشت — اختیاری" value={note} onChange={event => setNote(event.target.value)} placeholder="مثلاً: دو بسته به مسئول شعبه تحویل شد." />
    </label>

    <p className="return-finance-note">تسویه مالی نهایی پس از بسته‌شدن پرونده حادثه/برگشت تعیین می‌شود.</p>

    <div className="return-handoff-actions">
      <button onClick={() => onNotice('پشتیبانی تحویل برگشتی در این پیش‌نمایش فعال نیست.')}>پشتیبانی</button>
      <button className="return-submit-button" onClick={submitHandoff} disabled={submitted}>
        {submitted ? 'ثبت شد' : 'تحویل برگشتی ثبت شد'}
      </button>
    </div>
  </section>
}
