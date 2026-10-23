import { useCallback, useEffect, useMemo, useState } from 'react'
import './admissions-workbench.css'

type Customer = { customerId: string; legalName: string; displayName: string | null; status: string; createdAt: string }
type Courier = { courier_id: string; status: string; mode: string; created_at: string; updated_at: string }
type Row = { type: 'business' | 'courier'; id: string; name: string; status: string; createdAt: string; updatedAt: string }
type Props = { token: string; reloadKey: number; onConnected: (connected: boolean) => void; onOpenBusinesses: () => void; onOpenCouriers: () => void }
type ErrorBody = { error?: { code?: string; message?: string } }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
const stages: Record<string, string> = { PENDING: 'در انتظار بررسی', REGISTERED: 'ثبت‌شده', IDENTITY_VERIFIED: 'احراز هویت', DOCUMENTS_SUBMITTED: 'مدارک ارسال‌شده', TRUST_REQUIREMENTS_VERIFIED: 'شرایط اعتماد تکمیل', MONO_APPROVED: 'تأیید مونو', ACTIVE: 'فعال', REJECTED: 'ردشده' }

async function getJson<T>(path: string, token: string): Promise<T> {
  const response = await fetch(path, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
  if (!response.ok) {
    let message = `خطای سرور (${response.status})`
    try { const body = await response.json() as ErrorBody; message = body.error?.message || body.error?.code || message } catch { /* keep status message */ }
    throw new Error(message)
  }
  return response.json() as Promise<T>
}

function readableStatus(status: string) { return stages[status] || status }
function readableDate(value: string) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date) }

export default function AdmissionsWorkbench({ token, reloadKey, onConnected, onOpenBusinesses, onOpenCouriers }: Props) {
  const [rows, setRows] = useState<Row[]>([])
  const [searchDraft, setSearchDraft] = useState('')
  const [search, setSearch] = useState('')
  const [tab, setTab] = useState<'all' | 'courier' | 'business'>('all')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [sourceErrors, setSourceErrors] = useState<string[]>([])
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!token) { setRows([]); onConnected(false); return }
    setBusy(true); setError(''); setSourceErrors([])
    const query = search.trim()
    const businessParams = new URLSearchParams({ page_size: '100' })
    const courierParams = new URLSearchParams({ page_size: '100' })
    if (query) { businessParams.set('search', query); courierParams.set('query', query) }
    const [businessResult, courierResult] = await Promise.allSettled([
      getJson<{ customers: Customer[] }>(`/operations/v1/customers?${businessParams}`, token),
      getJson<{ items: Courier[] }>(`/operations/v1/couriers?${courierParams}`, token),
    ])
    const next: Row[] = []
    const failures: string[] = []
    if (businessResult.status === 'fulfilled' && Array.isArray(businessResult.value.customers)) {
      next.push(...businessResult.value.customers.map((item) => ({ type: 'business' as const, id: item.customerId, name: item.displayName || item.legalName, status: item.status, createdAt: item.createdAt, updatedAt: item.createdAt })))
    } else failures.push(`کسب‌وکارها: ${businessResult.status === 'rejected' ? (businessResult.reason instanceof Error ? businessResult.reason.message : 'دریافت ناموفق') : 'قرارداد پاسخ API نامعتبر است'}`)
    if (courierResult.status === 'fulfilled' && Array.isArray(courierResult.value.items)) {
      next.push(...courierResult.value.items.map((item) => ({ type: 'courier' as const, id: item.courier_id, name: item.courier_id, status: item.status, createdAt: item.created_at, updatedAt: item.updated_at })))
    } else failures.push(`پیک‌ها: ${courierResult.status === 'rejected' ? (courierResult.reason instanceof Error ? courierResult.reason.message : 'دریافت ناموفق') : 'قرارداد پاسخ API نامعتبر است'}`)
    setRows(next); setSourceErrors(failures); setError(failures.length === 2 ? 'دریافت هر دو منبع ناموفق بود.' : '')
    onConnected(failures.length < 2); setUpdatedAt(new Date().toISOString()); setBusy(false)
  }, [onConnected, search, token])

  useEffect(() => { void load() }, [load, reloadKey])
  const filtered = useMemo(() => rows.filter((row) => tab === 'all' || row.type === tab), [rows, tab])
  const counts = useMemo(() => ({ businesses: rows.filter((row) => row.type === 'business').length, couriers: rows.filter((row) => row.type === 'courier').length, pending: rows.filter((row) => row.status !== 'ACTIVE' && row.status !== 'REJECTED').length, earlyStage: rows.filter((row) => row.status === 'PENDING' || row.status === 'REGISTERED' || row.status === 'DOCUMENTS_SUBMITTED').length, readyCouriers: rows.filter((row) => row.type === 'courier' && row.status === 'MONO_APPROVED').length }), [rows])

  function submitSearch(event: React.FormEvent) { event.preventDefault(); setSearch(searchDraft.trim()) }

  return <main className="admissions-workbench">
    <div className="admissions-intro"><div><span className="eyebrow">پذیرش و راه‌اندازی</span><h2>صف یکپارچهٔ پذیرش</h2><p>داده‌ها از صف مشتری‌ها و پیک‌ها خوانده می‌شوند. اعداد، فقط رکوردهای دریافت‌شده در این صفحه را نشان می‌دهند.</p></div><button className="secondary-button" onClick={() => void load()} disabled={!token || busy}>{busy ? 'در حال دریافت…' : 'تازه‌سازی'}</button></div>
    {!token ? <div className="admissions-empty">برای مشاهدهٔ صف‌ها با توکن مدیر عملیات متصل شوید.</div> : <>
      <section className="admissions-metrics" aria-label="خلاصه صف پذیرش">
        <article><span>پرونده در این صفحه</span><strong>{fa.format(rows.length)}</strong><small>دادهٔ فعلی API</small></article>
        <article><span>پیک</span><strong>{fa.format(counts.couriers)}</strong><small>از صف پیک‌ها</small></article>
        <article><span>کسب‌وکار</span><strong>{fa.format(counts.businesses)}</strong><small>از صف مشتری‌ها</small></article>
        <article><span>در جریان پذیرش</span><strong>{fa.format(counts.pending)}</strong><small>به‌جز فعال و ردشده</small></article>
        <article><span>در مراحل اولیه</span><strong>{fa.format(counts.earlyStage)}</strong><small>PENDING، REGISTERED یا DOCUMENTS_SUBMITTED</small></article>
        <article><span>پیک آمادهٔ فعال‌سازی</span><strong>{fa.format(counts.readyCouriers)}</strong><small>وضعیت صریح MONO_APPROVED</small></article>
      </section>
      {sourceErrors.length > 0 && <div className="admissions-warning" role="status">{sourceErrors.map((message) => <p key={message}>{message}</p>)}</div>}
      {error && <div className="onboarding-error" role="alert">{error}</div>}
      <section className="admissions-card">
        <div className="admissions-toolbar"><div className="admissions-tabs" role="tablist" aria-label="نوع پرونده">{([['all', 'همه'], ['courier', 'پیک‌ها'], ['business', 'کسب‌وکارها']] as const).map(([value, label]) => <button key={value} role="tab" aria-selected={tab === value} className={tab === value ? 'selected' : ''} onClick={() => setTab(value)}>{label}</button>)}</div><form onSubmit={submitSearch}><label htmlFor="admissions-search">جست‌وجو در شناسه یا نام</label><input id="admissions-search" value={searchDraft} onChange={(event) => setSearchDraft(event.target.value)} maxLength={128} /><button className="secondary-button" disabled={busy}>جست‌وجو</button></form></div>
        <div className="admissions-table-wrap"><table><thead><tr><th>نوع پرونده</th><th>شناسه</th><th>نام</th><th>مرحلهٔ ثبت‌شده</th><th>آخرین زمان قابل‌دسترسی</th><th>اقدام</th></tr></thead><tbody>
          {filtered.map((row) => <tr key={`${row.type}-${row.id}`}><td>{row.type === 'courier' ? 'پیک' : 'کسب‌وکار'}</td><td dir="ltr">{row.id}</td><td>{row.name}</td><td><span className={`admissions-status ${row.status === 'ACTIVE' ? 'active' : ''}`}>{readableStatus(row.status)}</span></td><td>{readableDate(row.updatedAt || row.createdAt)}</td><td><button className="text-button" onClick={row.type === 'courier' ? onOpenCouriers : onOpenBusinesses}>رفتن به پرونده‌ها</button></td></tr>)}
          {filtered.length === 0 && <tr><td colSpan={6} className="admissions-no-rows">{busy ? 'در حال دریافت از API…' : 'برای این فیلتر، رکوردی در صفحهٔ دریافت‌شده وجود ندارد.'}</td></tr>}
        </tbody></table></div>
        <footer className="admissions-footnote">صفحهٔ نخست هر منبع حداکثر ۱۰۰ رکورد را می‌خواند؛ برای جست‌وجو از فیلتر API هر منبع استفاده می‌شود. آخرین به‌روزرسانی: {updatedAt ? readableDate(updatedAt) : '—'}</footer>
      </section>
      <section className="admissions-card admissions-process"><div><h3>مراحل پیک در API فعلی</h3><p>ثبت‌شده ← احراز هویت ← ارسال مدارک ← تکمیل شرایط اعتماد ← تأیید Mono ← فعال</p></div><div><h3>مراحل کسب‌وکار در API فعلی</h3><p>ثبت PENDING ← فعال‌سازی مشتری ← ثبت شعبه‌ها و پیکربندی سرویس</p></div><p className="admissions-disclaimer">مدارک خام، قرارداد/مالی و اطلاعات روش اتصال به‌عنوان مراحل مستقل در API فعلی وجود ندارند؛ وضعیت آن‌ها از این صفحه ساخته یا حدس زده نمی‌شود.</p></section>
    </>}
  </main>
}
