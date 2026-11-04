import { useCallback, useEffect, useRef, useState } from 'react'
import './audit-workbench.css'

type AuditSource = 'mission' | 'courier' | 'route' | 'webhook'
type AuditRow = { id: string; actor: string; action: string; resource: string; outcome: string; count: number; occurredAt: string }
type AuditPage = { items: AuditRow[]; nextCursor: string | null }
type Props = { token: string; reloadKey: number; onConnected: (connected: boolean) => void }
type ApiError = { error?: { code?: string; message?: string }; error_code?: string }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
const sourceNames: Record<AuditSource, string> = { mission: 'مأموریت‌ها', courier: 'پیک‌ها', route: 'مسیرها', webhook: 'وب‌هوک‌ها' }

function formatDate(value: string) {
  const parsed = new Date(value)
  return Number.isNaN(parsed.valueOf()) ? '—' : dateTime.format(parsed)
}

function localDateInput(date: Date) {
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

async function readError(response: Response) {
  try {
    const body = await response.json() as ApiError
    return body.error?.message || body.error?.code || body.error_code || `خطای سرور (${response.status})`
  } catch { return `خطای سرور (${response.status})` }
}

function auditRows(source: AuditSource, raw: Record<string, unknown>[]): AuditRow[] {
  return raw.map((item, index) => {
    const resource = source === 'mission' ? item.mission_id : source === 'courier' ? item.courier_id : source === 'route' ? item.route_id : item.resource_id
    const outcome = source === 'route' ? item.outcome : source === 'webhook' ? item.outcome_code : `${item.result_count ?? 0} رکورد`
    return {
      id: String(item.audit_id || `${source}-${index}`), actor: String(item.actor_id || '—'),
      action: String(item.action || '—'), resource: String(resource || '—'),
      outcome: String(outcome || '—'), count: Number(item.result_count || 0),
      occurredAt: String(item.occurred_at || ''),
    }
  })
}

export default function AuditWorkbench({ token, reloadKey, onConnected }: Props) {
  const [source, setSource] = useState<AuditSource>('mission')
  const [resourceId, setResourceId] = useState('')
  const [actorId, setActorId] = useState('')
  const [action, setAction] = useState('')
  const [fromAt, setFromAt] = useState(() => localDateInput(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000)))
  const [toAt, setToAt] = useState(() => localDateInput(new Date()))
  const [page, setPage] = useState<AuditPage | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const requestSequence = useRef(0)

  const load = useCallback(async (cursor?: string | null, stack?: Array<string | null>) => {
    if (!token) return
    if (source === 'route' && (!fromAt || !toAt || !Number.isFinite(new Date(fromAt).valueOf()) || !Number.isFinite(new Date(toAt).valueOf()))) {
      setError('برای بررسی سابقهٔ مسیر، بازهٔ زمانی معتبر وارد کنید.')
      return
    }
    const requestId = ++requestSequence.current
    setLoading(true); setError('')
    const query = new URLSearchParams()
    let path = ''
    if (source === 'mission') {
      path = '/operations/v1/mission-read-audit'
      query.set('page_size', '50')
      if (resourceId.trim()) query.set('mission_id', resourceId.trim())
      if (actorId.trim()) query.set('actor_id', actorId.trim())
      if (fromAt) query.set('created_after', new Date(fromAt).toISOString())
      if (toAt) query.set('created_before', new Date(toAt).toISOString())
    } else if (source === 'courier') {
      path = '/operations/v1/courier-read-audit'
      query.set('page_size', '50')
    } else if (source === 'route') {
      path = '/operations/v1/routes/plan-read-audit'
      query.set('limit', '50')
      query.set('from_at', new Date(fromAt).toISOString())
      query.set('to_at', new Date(toAt).toISOString())
      if (resourceId.trim()) query.set('route_id', resourceId.trim())
      if (action) query.set('action', action)
    } else {
      path = '/operations/v1/webhook-deliveries/audit'
      query.set('limit', '50')
      if (actorId.trim()) query.set('actor_id', actorId.trim())
      if (action) query.set('action', action)
    }
    if (cursor) query.set('cursor', cursor)
    try {
      const response = await fetch(`${path}?${query}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      const result = await response.json() as { items?: Record<string, unknown>[]; next_cursor?: string | null; nextCursor?: string | null }
      if (!Array.isArray(result.items)) throw new Error('ساختار پاسخ ممیزی با قرارداد API سازگار نیست.')
      if (requestId !== requestSequence.current) return
      setPage({ items: auditRows(source, result.items), nextCursor: result.next_cursor || result.nextCursor || null })
      setCursorStack(stack || [null])
      onConnected(true)
    } catch (cause) {
      if (requestId !== requestSequence.current) return
      setPage(null); setError(cause instanceof Error ? cause.message : 'دریافت سابقهٔ ممیزی ناموفق بود.')
      onConnected(false)
    } finally { if (requestId === requestSequence.current) setLoading(false) }
  }, [action, actorId, fromAt, onConnected, resourceId, source, toAt, token])

  const loadRef = useRef(load)
  loadRef.current = load
  useEffect(() => { if (token) void loadRef.current(null, [null]) }, [reloadKey, source, token])

  function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (fromAt && toAt && new Date(fromAt) >= new Date(toAt)) { setError('ابتدای بازه باید قبل از پایان آن باشد.'); return }
    if (source === 'route' && (!fromAt || !toAt)) { setError('برای بررسی سابقهٔ مسیر، ابتدا و انتهای بازه را وارد کنید.'); return }
    void load(null, [null])
  }

  function changeSource(next: AuditSource) {
    setSource(next); setPage(null); setCursorStack([null]); setError('')
  }

  function nextPage() {
    if (!page?.nextCursor) return
    const stack = [...cursorStack, page.nextCursor]
    void load(page.nextCursor, stack)
  }

  function previousPage() {
    if (cursorStack.length < 2) return
    const stack = cursorStack.slice(0, -1)
    void load(stack.at(-1) || null, stack)
  }

  const resourceLabel = source === 'mission' ? 'شناسهٔ مأموریت' : source === 'route' ? 'شناسهٔ مسیر' : source === 'courier' ? 'شناسهٔ پیک' : ''
  const actionOptions: Record<AuditSource, Array<[string, string]>> = {
    mission: [], courier: [],
    route: [['ROUTE_PLAN_CURRENT_READ', 'خواندن برنامهٔ جاری'], ['ROUTE_PLAN_VERSION_READ', 'خواندن نسخه'], ['ROUTE_PLAN_AUDIT_INSPECTION', 'بررسی ممیزی']],
    webhook: [['QUEUE_SUMMARY', 'خلاصهٔ صف'], ['QUEUE_SEARCH', 'جست‌وجوی صف'], ['DELIVERY_DETAIL', 'جزئیات تحویل'], ['DELIVERY_REPLAY', 'ارسال مجدد'], ['DELIVERY_CANCEL', 'لغو تحویل'], ['AUDIT_HISTORY_READ', 'خواندن سابقه'], ['WORKER_STATUS_READ', 'وضعیت پردازش']],
  }

  return <main className="audit-workbench">
    <section className="queue-panel audit-intro">
      <div className="panel-heading"><div><span className="eyebrow">سابقهٔ عملیاتی</span><h2>ممیزی دامنه‌ای</h2></div><span className="panel-meta">فقط خواندن · ثبت‌ها حذف یا ویرایش نمی‌شوند</span></div>
      <div className="audit-source-tabs" role="tablist" aria-label="دامنهٔ سابقهٔ ممیزی">
        {(Object.entries(sourceNames) as Array<[AuditSource, string]>).map(([value, label]) => <button type="button" role="tab" aria-selected={source === value} className={source === value ? 'active' : ''} key={value} onClick={() => changeSource(value)}>{label}</button>)}
      </div>
      <form className="audit-filters" onSubmit={submit}>
        {resourceLabel && <label>{resourceLabel}<input value={resourceId} onChange={(event) => setResourceId(event.target.value)} dir="ltr" maxLength={128} autoComplete="off" /></label>}
        {(source === 'mission' || source === 'webhook') && <label>عامل<input value={actorId} onChange={(event) => setActorId(event.target.value)} dir="ltr" maxLength={128} autoComplete="off" /></label>}
        {(source === 'route' || source === 'webhook') && <label>عمل<select value={action} onChange={(event) => setAction(event.target.value)}><option value="">همهٔ عملیات</option>{actionOptions[source].map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>}
        {(source === 'mission' || source === 'route') && <>
          <label>از<input type="datetime-local" value={fromAt} onChange={(event) => setFromAt(event.target.value)} /></label>
          <label>تا<input type="datetime-local" value={toAt} onChange={(event) => setToAt(event.target.value)} /></label>
        </>}
        <button className="secondary-button" disabled={!token || loading}>{loading ? 'در حال دریافت…' : 'اعمال فیلتر'}</button>
      </form>
      <p className="audit-scope-note">این صفحه جست‌وجوی یکپارچهٔ همهٔ رویدادها نیست. API فعلی برای هر دامنه فیلتر و نگهداری متفاوت دارد؛ فقط سابقهٔ قابل‌دسترسی همان دامنه نمایش داده می‌شود.</p>
      {!token && <p className="audit-scope-note">برای مشاهدهٔ سابقه با توکن مدیر عملیات متصل شوید.</p>}
      {error && <div className="onboarding-error" role="alert">{error}</div>}
    </section>
    <section className="queue-panel audit-results">
      <div className="panel-heading"><div><span className="eyebrow">{sourceNames[source]}</span><h2>رویدادهای ثبت‌شده</h2></div><span className="panel-meta">{page ? `${fa.format(page.items.length)} رویداد` : 'بدون دادهٔ نمونه'}</span></div>
      <div className="table-wrap"><table><thead><tr><th>زمان</th><th>عامل</th><th>عمل</th><th>شناسهٔ مرتبط</th><th>نتیجه</th><th>تعداد</th></tr></thead><tbody>
        {loading && <tr><td colSpan={6} className="state-cell">در حال دریافت رویدادها از API…</td></tr>}
        {!loading && page?.items.map((item) => <tr key={item.id}><td>{formatDate(item.occurredAt)}</td><td dir="ltr">{item.actor}</td><td dir="ltr">{item.action}</td><td dir="ltr">{item.resource}</td><td>{item.outcome}</td><td>{fa.format(item.count)}</td></tr>)}
        {!loading && page && page.items.length === 0 && <tr><td colSpan={6} className="state-cell">در این دامنه و بازه، رویدادی برنگشت.</td></tr>}
        {!loading && !page && !error && <tr><td colSpan={6} className="state-cell">برای دریافت سابقه، اتصال به API را برقرار کنید.</td></tr>}
      </tbody></table></div>
      <footer className="table-footer"><span>صفحهٔ {fa.format(cursorStack.length)}</span><div className="pagination"><button onClick={previousPage} disabled={!token || loading || cursorStack.length < 2}>قبلی</button><button onClick={nextPage} disabled={!token || loading || !page?.nextCursor}>بعدی</button></div></footer>
    </section>
  </main>
}
