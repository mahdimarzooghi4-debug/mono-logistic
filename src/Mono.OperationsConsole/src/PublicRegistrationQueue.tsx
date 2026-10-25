import { useCallback, useEffect, useState } from 'react'
import './public-registration-queue.css'

type Application = {
  requestId: string
  type: 'BUSINESS' | 'COURIER'
  status: 'SUBMITTED' | 'CONTACT_ATTEMPTED' | string
  fullName: string
  phone: string
  businessName: string | null
  privacyNoticeVersion: string
  submittedAt: string
}
type Props = { token: string }
const numberFa = new Intl.NumberFormat('fa-IR')
const dateFa = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
const labels: Record<string, string> = { SUBMITTED: 'در انتظار تماس', CONTACT_ATTEMPTED: 'تماس انجام شده' }

async function responseError(response: Response) {
  try {
    const body = await response.json() as { error?: string }
    return body.error || `خطای سرور (${response.status})`
  } catch { return `خطای سرور (${response.status})` }
}

export default function PublicRegistrationQueue({ token }: Props) {
  const [items, setItems] = useState<Application[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [decliningId, setDecliningId] = useState('')
  const [reason, setReason] = useState('')

  const load = useCallback(async () => {
    if (!token) { setItems([]); return }
    setBusy(true); setError(''); setNotice('')
    try {
      const response = await fetch('/operations/v1/public-registration-requests?page_size=100', {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store',
      })
      if (!response.ok) throw new Error(await responseError(response))
      const body = await response.json() as Application[]
      setItems(Array.isArray(body) ? body : [])
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'دریافت درخواست‌ها ناموفق بود.')
    } finally { setBusy(false) }
  }, [token])

  useEffect(() => { void load() }, [load])

  async function transition(item: Application, status: string, reasonCode?: string) {
    setBusy(true); setError(''); setNotice('')
    try {
      const response = await fetch(`/operations/v1/public-registration-requests/${encodeURIComponent(item.requestId)}/transition`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ status, reason_code: reasonCode || null }),
      })
      if (!response.ok) throw new Error(await responseError(response))
      setDecliningId(''); setReason('')
      await load()
      setNotice('وضعیت درخواست به‌روزرسانی شد.')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'ثبت تغییر وضعیت ناموفق بود.')
    } finally { setBusy(false) }
  }

  return <section className="public-registration-queue" aria-labelledby="public-registration-title">
    <header>
      <div><span className="eyebrow">درخواست‌های جدید</span><h2 id="public-registration-title">همکاری‌های منتظر پیگیری</h2>
        <p>این درخواست‌ها هنوز حساب کسب‌وکار یا سفیر فعال نیستند. اطلاعات تماس فقط برای پیگیری نمایش داده می‌شود.</p></div>
      <button className="secondary-button" type="button" onClick={() => void load()} disabled={busy}>{busy ? 'در حال دریافت…' : 'تازه‌سازی'}</button>
    </header>
    {error && <div className="onboarding-error" role="alert">{error}</div>}
    {notice && <div className="onboarding-notice" role="status">{notice}</div>}
    <div className="public-registration-count">درخواست‌های بارگیری‌شده: <strong>{numberFa.format(items.length)}</strong> <small>حداکثر ۱۰۰ مورد</small></div>
    <div className="public-registration-table-wrap"><table><thead><tr>
      <th>نوع</th><th>نام</th><th>تلفن</th><th>کسب‌وکار</th><th>زمان ثبت</th><th>وضعیت</th><th>پیگیری</th>
    </tr></thead><tbody>
      {items.map(item => <tr key={item.requestId}>
        <td>{item.type === 'COURIER' ? 'سفیر' : 'کسب‌وکار'}</td>
        <td>{item.fullName}</td><td dir="ltr">{item.phone}</td><td>{item.businessName || '—'}</td>
        <td>{dateFa.format(new Date(item.submittedAt))}</td><td>{labels[item.status] || item.status}</td>
        <td><div className="public-registration-actions">
          <button type="button" className="text-button" disabled={busy || item.status === 'CONTACT_ATTEMPTED'} onClick={() => void transition(item, 'CONTACT_ATTEMPTED')}>ثبت تماس</button>
          <button type="button" className="text-button" disabled={busy} onClick={() => void transition(item, 'CLOSED')}>بستن</button>
          <button type="button" className="text-button danger" disabled={busy} onClick={() => { setDecliningId(item.requestId); setReason('') }}>رد</button>
        </div>
          {decliningId === item.requestId && <form className="public-registration-decline" onSubmit={event => { event.preventDefault(); if (reason.trim()) void transition(item, 'DECLINED', reason.trim()) }}>
            <label>کد دلیل رد<input value={reason} onChange={event => setReason(event.target.value)} maxLength={80} required /></label>
            <button type="submit" className="text-button" disabled={busy || !reason.trim()}>ثبت رد</button>
            <button type="button" className="text-button" onClick={() => setDecliningId('')}>لغو</button>
          </form>}
        </td>
      </tr>)}
      {items.length === 0 && <tr><td colSpan={7} className="public-registration-empty">{busy ? 'در حال دریافت…' : 'درخواست فعالی برای پیگیری وجود ندارد.'}</td></tr>}
    </tbody></table></div>
    <footer>نسخهٔ اطلاعیهٔ حریم خصوصی هنگام ثبت ذخیره شده و در سوابق درخواست قابل پیگیری است.</footer>
  </section>
}
