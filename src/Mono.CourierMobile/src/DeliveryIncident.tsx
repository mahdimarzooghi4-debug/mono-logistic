import { useRef, useState } from 'react'
import type { ChangeEvent } from 'react'

type DeliveryIncidentProps = { onNotice: (message: string) => void; onRegistered: () => void }
type IncidentType = 'damage' | 'missing' | 'opened' | 'other'

const incidentOptions: { id: IncidentType; label: string }[] = [
  { id: 'damage', label: 'آسیب‌دیدگی' },
  { id: 'missing', label: 'کسری / مفقودی' },
  { id: 'opened', label: 'بسته باز شده' },
  { id: 'other', label: 'مورد دیگر' },
]
const incidentLabels: Record<IncidentType, string> = {
  damage: 'آسیب ظاهری',
  missing: 'کسری / مفقودی',
  opened: 'بازشدگی بسته',
  other: 'نیازمند بررسی',
}

export default function DeliveryIncident({ onNotice, onRegistered }: DeliveryIncidentProps) {
  const [type, setType] = useState<IncidentType>('damage')
  const [packageNumber, setPackageNumber] = useState('1')
  const [explanationOpen, setExplanationOpen] = useState(false)
  const [explanation, setExplanation] = useState('')
  const [evidenceName, setEvidenceName] = useState('')
  const [registered, setRegistered] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)

  function selectEvidence(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (file) {
      setEvidenceName(file.name)
      onNotice('تصویر فقط در این پیش‌نمایش انتخاب شد و بارگذاری نمی‌شود.')
    }
  }

  function submitIncident() {
    if (registered) return
    if (!explanation.trim() && !evidenceName) {
      onNotice('برای ثبت حادثه، یک توضیح یا تصویر اضافه کن.')
      setExplanationOpen(true)
      return
    }
    setRegistered(true)
    onNotice('گزارش حادثه در پیش‌نمایش ثبت شد؛ به پشتیبانی متصل نیست.')
    onRegistered()
  }

  return <section className="content-scroll delivery-incident-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>مشکل در تحویل</strong><small>MIS-۱۰۴۹۸ • ثبت حادثه</small></div>
    </header>

    <section className="incident-status-card">
      <div className="incident-status-head"><span className={registered ? 'closed' : ''}>{registered ? 'ثبت شده' : 'حادثه باز'}</span><strong>علی رضایی</strong></div>
      <p>HNA-۸۳۹۲۵۱ • ۲ بسته • تحویل در مقصد</p>
      <div className="incident-status-metrics">
        <div><small>وضعیت</small><b className="incident-red">{registered ? 'ثبت شد' : 'نیازمند بررسی'}</b></div>
        <div><small>بسته</small><b>۲</b></div>
        <div><small>مرحله</small><b>تحویل</b></div>
      </div>
    </section>

    <section className="incident-type-card">
      <h1>نوع مشکل</h1>
      <div className="incident-type-grid" role="radiogroup" aria-label="نوع مشکل">
        {incidentOptions.map(option => <label key={option.id} className={type === option.id ? 'selected' : ''}>
          <input type="radio" name="incident-type" checked={type === option.id} onChange={() => setType(option.id)} />
          <span>{option.label}</span>
        </label>)}
      </div>
    </section>

    <section className="incident-details-card">
      <h2>جزئیات مورد</h2>
      <div className="incident-detail-row">
        <select aria-label="بسته درگیر" value={packageNumber} onChange={event => setPackageNumber(event.target.value)}>
          <option value="1">بسته ۱ از ۲</option><option value="2">بسته ۲ از ۲</option>
        </select><span>بسته درگیر</span>
      </div>
      <div className="incident-detail-row"><strong className="incident-red">{incidentLabels[type]}</strong><span>وضعیت</span></div>
      <div className="incident-detail-row"><strong className="incident-warning">متوقف تا بررسی</strong><span>تحویل به گیرنده</span></div>
      <div className="incident-detail-row"><strong>۱۲:۲۸</strong><span>زمان ثبت</span></div>
    </section>

    <section className={`incident-evidence-card ${explanationOpen ? 'with-explanation' : ''}`}>
      <h2>مدرک و توضیح</h2>
      <p>{evidenceName ? `تصویر انتخاب‌شده: ${evidenceName}` : 'تصویر یا توضیح کوتاه برای بررسی پشتیبانی ثبت کن.'}</p>
      <div className="incident-evidence-actions">
        <button onClick={() => fileInput.current?.click()}>{evidenceName ? 'تغییر تصویر' : 'افزودن تصویر'}</button>
        <button onClick={() => setExplanationOpen(open => !open)}>{explanationOpen ? 'بستن توضیح' : 'افزودن توضیح'}</button>
        <input ref={fileInput} type="file" accept="image/*" aria-label="انتخاب تصویر حادثه" onChange={selectEvidence} />
      </div>
      {explanationOpen && <textarea aria-label="توضیح حادثه" value={explanation} onChange={event => setExplanation(event.target.value)} placeholder="شرح کوتاه حادثه را بنویس…" />}
    </section>

    <p className="incident-guidance">بعد از تحویل سفارش به پیک، لغو مستقیم در دسترس نیست؛ پرونده از مسیر حادثه و پشتیبانی ادامه پیدا می‌کند.</p>
    <div className="incident-actions">
      <button onClick={() => onNotice('درخواست پشتیبانی حادثه در پیش‌نمایش ثبت شد؛ اتصال پشتیبانی فعال نیست.')}>پشتیبانی</button>
      <button className="incident-submit" onClick={submitIncident} disabled={registered}>{registered ? 'حادثه ثبت شد' : 'ثبت حادثه'}</button>
    </div>
  </section>
}
