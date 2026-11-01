import { useState } from 'react'

type FinalDeliveryProps = { onNotice: (message: string) => void; onComplete: () => void; onUnavailable: () => void }

export default function FinalDelivery({ onNotice, onComplete, onUnavailable }: FinalDeliveryProps) {
  const [recipientType, setRecipientType] = useState<'customer' | 'other'>('customer')
  const [recipient, setRecipient] = useState('علی رضایی')
  const [condition, setCondition] = useState<'intact' | 'issue'>('intact')
  const [note, setNote] = useState('')
  const [delivered, setDelivered] = useState(false)

  function completeDelivery() {
    if (!recipient.trim()) {
      onNotice('نام گیرنده را وارد کنید.')
      return
    }
    setDelivered(true)
    onNotice('تحویل سفارش ثبت شد.')
    onComplete()
  }

  return <section className="content-scroll final-delivery-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>تحویل نهایی</strong><small>MIS-۱۰۴۹۸ • رسیدن به مقصد</small></div>
    </header>

    <section className="final-arrived-summary">
      <div className="final-summary-head"><strong>علی رضایی</strong><span className="final-arrived-pill">رسیده‌ام</span></div>
      <p>یوسف‌آباد، خیابان شصت‌وچهارم، پلاک ۲۱</p>
      <div className="final-summary-metrics">
        <div><small>استاندارد خدمت</small><b className="safe">ایمن</b></div>
        <div><small>بسته</small><b>۲</b></div>
        <div><small>وضعیت</small><b className="pending">{delivered ? 'تحویل شد' : 'در انتظار تحویل'}</b></div>
      </div>
    </section>

    <section className="final-recipient-card">
      <h2>تأیید گیرنده</h2>
      <p className="final-helper">نام شخصی که بسته را تحویل می‌گیرد ثبت کن. برای MVP رمز یک‌بارمصرف لازم نیست.</p>
      <label className="recipient-name-field">
        <span>نام گیرنده</span>
        <input value={recipient} onChange={event => setRecipient(event.target.value)} aria-label="نام گیرنده" />
      </label>
      <div className="final-choice-row" role="radiogroup" aria-label="نسبت گیرنده با سفارش‌دهنده">
        <label className={recipientType === 'customer' ? 'selected' : ''}>
          <input type="radio" name="recipient-type" checked={recipientType === 'customer'} onChange={() => setRecipientType('customer')} />
          <span>خود سفارش‌دهنده</span>
        </label>
        <label className={recipientType === 'other' ? 'selected' : ''}>
          <input type="radio" name="recipient-type" checked={recipientType === 'other'} onChange={() => setRecipientType('other')} />
          <span>شخص دیگر</span>
        </label>
      </div>
    </section>

    <section className="final-package-card">
      <div className="final-package-heading"><h2>وضعیت بسته هنگام تحویل</h2><span>۲ بسته</span></div>
      <div className="final-choice-row" role="radiogroup" aria-label="وضعیت بسته">
        <label className={condition === 'intact' ? 'selected' : ''}>
          <input type="radio" name="package-condition" checked={condition === 'intact'} onChange={() => setCondition('intact')} />
          <span>بسته سالم تحویل شد</span>
        </label>
        <label className={condition === 'issue' ? 'selected' : ''}>
          <input type="radio" name="package-condition" checked={condition === 'issue'} onChange={() => setCondition('issue')} />
          <span>آسیب/کسری</span>
        </label>
      </div>
      <div className="final-order-id"><span>HNA-۸۳۹۲۵۱</span><small>شماره سفارش</small></div>
    </section>

    <label className="final-delivery-note">
      <span>یادداشت تحویل — اختیاری</span>
      <input value={note} onChange={event => setNote(event.target.value)} placeholder="مثلاً: تحویل به خود سفارش‌دهنده در ورودی ساختمان" aria-label="یادداشت تحویل اختیاری" />
    </label>

    <div className="final-issue-actions">
      <button className="unavailable" onClick={onUnavailable}>گیرنده در دسترس نیست</button>
      <button onClick={() => onNotice('گزارش مشکل تحویل برای این مأموریت ثبت شد.')}>مشکل در تحویل</button>
    </div>
    <button className="final-complete-button" onClick={completeDelivery} disabled={delivered}>{delivered ? 'تحویل ثبت شد' : 'تحویل انجام شد'}</button>
  </section>
}
