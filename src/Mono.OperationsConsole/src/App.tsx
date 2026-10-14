import { useCallback, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Offer = {
  offerId?: string
  courierId?: string
  status?: string
  expiresAt?: string | null
  deadlinePassed?: boolean
}

type Attempt = {
  action?: string
  outcome?: string
  createdAt?: string | null
}

type Mission = {
  missionId: string
  customerId: string
  branchId: string
  createdAt: string
  committedDeliveryAt: string
  activeOffer: Offer | null
  attemptCount: number
  latestAttempt: Attempt | null
  deliveryOverdue: boolean
  deliveryWindowState: 'upcoming' | 'due_now' | 'overdue' | string
}

type QueuePage = { items: Mission[]; nextCursor: string | null }
type MissionRecord = {
  missionId: string
  customerId: string
  deliveryRequestId: string
  status: string
  committedDeliveryAt: string
  createdAt: string
}
type MissionPage = { items: MissionRecord[]; nextCursor: string | null }
type MissionTimeline = { events?: Array<{ eventId: string; eventType: string; sequence: number; occurredAt: string }> }
type MissionReadAudit = { items?: Array<{ auditId: string; actorId: string; action: string; missionId: string | null; resultCount: number; occurredAt: string }> }
type CourierSummary = { courier_id: string; mode: string; status: string; preferred_zone_ids: string[]; approved_zone_ids: string[]; primary_zone_id: string | null; created_at: string; updated_at: string }
type CourierPage = { items: CourierSummary[]; next_cursor: string | null }
type CourierDetail = CourierSummary & { created_by: string; trust_transitions: Array<{ transition_id: string; from_status: string; to_status: string; actor_id: string; reason_code: string; evidence_reference: string; occurred_at: string }> }
type CourierReadAudit = { items?: Array<{ audit_id: string; actor_id: string; action: string; courier_id: string | null; result_count: number; occurred_at: string }> }
type ApiError = { error?: { code?: string; message?: string } }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', {
  dateStyle: 'short',
  timeStyle: 'short',
})

function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date)
}

function statusLabel(state: string) {
  if (state === 'overdue') return 'از موعد گذشته'
  if (state === 'due_now') return 'موعد همین حالا'
  return 'در بازه'
}

async function readError(response: Response) {
  try {
    const body = (await response.json()) as ApiError
    return body.error?.message || body.error?.code || `خطای سرور (${response.status})`
  } catch {
    return `پاسخ نامعتبر از سرور (${response.status})`
  }
}

export default function App() {
  const [viewMode, setViewMode] = useState<'dispatch' | 'missions' | 'couriers'>('dispatch')
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [items, setItems] = useState<Mission[]>([])
  const [missionItems, setMissionItems] = useState<MissionRecord[]>([])
  const [courierItems, setCourierItems] = useState<CourierSummary[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [missionNextCursor, setMissionNextCursor] = useState<string | null>(null)
  const [courierNextCursor, setCourierNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [pageIndex, setPageIndex] = useState(0)
  const [query, setQuery] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [hasOffer, setHasOffer] = useState('')
  const [deliveryWindow, setDeliveryWindow] = useState('')
  const [createdBefore, setCreatedBefore] = useState('')
  const [createdAfter, setCreatedAfter] = useState('')
  const [sortBy, setSortBy] = useState('delivery_due')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [connected, setConnected] = useState(false)
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [missionDetail, setMissionDetail] = useState<MissionRecord | null>(null)
  const [missionTimeline, setMissionTimeline] = useState<NonNullable<MissionTimeline['events']>>([])
  const [missionAudit, setMissionAudit] = useState<NonNullable<MissionReadAudit['items']>>([])
  const [courierDetail, setCourierDetail] = useState<CourierDetail | null>(null)
  const [courierAudit, setCourierAudit] = useState<NonNullable<CourierReadAudit['items']>>([])
  const [detailErrors, setDetailErrors] = useState<string[]>([])
  const [timelineTitle, setTimelineTitle] = useState('')
  const [timelineLoading, setTimelineLoading] = useState(false)
  const detailRequestId = useRef(0)

  const totals = useMemo(() => ({
    rows: items.length,
    offers: items.filter((item) => item.activeOffer).length,
    overdue: items.filter((item) => item.deliveryOverdue).length,
    lateOffers: items.filter((item) => item.activeOffer?.deadlinePassed).length,
  }), [items])

  const fetchPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setItems([])
    const params = new URLSearchParams({ page_size: '25', sort_by: sortBy })
    if (query.trim()) params.set('query', query.trim())
    if (customerId.trim()) params.set('customer_id', customerId.trim())
    if (hasOffer) params.set('has_offer', hasOffer)
    if (deliveryWindow) params.set('delivery_window_state', deliveryWindow)
    if (createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/dispatch/queue?${params}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as QueuePage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ صف مأموریت با قرارداد API سازگار نیست.')
      setItems(page.items)
      setNextCursor(page.nextCursor || null)
      setCursorStack(nextStack || [null])
      setPageIndex((nextStack || [null]).length - 1)
      setConnected(true)
      setUpdatedAt(new Date().toISOString())
    } catch (cause) {
      setConnected(false)
      setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.')
    } finally {
      setLoading(false)
    }
  }, [token, sortBy, query, customerId, hasOffer, deliveryWindow, createdBefore])

  const fetchMissionPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setMissionItems([])
    const params = new URLSearchParams({ page_size: '25' })
    if (query.trim()) params.set('query', query.trim())
    if (createdAfter) params.set('created_after', new Date(createdAfter).toISOString())
    if (createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/missions?${params}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as MissionPage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ فهرست مأموریت با قرارداد API سازگار نیست.')
      setMissionItems(page.items)
      setMissionNextCursor(page.nextCursor || null)
      setCursorStack(nextStack || [null])
      setPageIndex((nextStack || [null]).length - 1)
      setConnected(true)
      setUpdatedAt(new Date().toISOString())
    } catch (cause) {
      setConnected(false)
      setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.')
    } finally {
      setLoading(false)
    }
  }, [token, query, createdAfter, createdBefore])

  const fetchCourierPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) { setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.'); return }
    setLoading(true); setError(''); setCourierItems([])
    const params = new URLSearchParams({ page_size: '25' })
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/couriers?${params}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      const page = await response.json() as CourierPage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ فهرست پیک‌ها با قرارداد API سازگار نیست.')
      setCourierItems(page.items); setCourierNextCursor(page.next_cursor || null)
      setCursorStack(nextStack || [null]); setPageIndex((nextStack || [null]).length - 1)
      setConnected(true); setUpdatedAt(new Date().toISOString())
    } catch (cause) { setConnected(false); setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.') }
    finally { setLoading(false) }
  }, [token])

  function connect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setToken(tokenDraft.trim())
    setTokenDraft('')
    setItems([])
    setMissionItems([])
    setCourierItems([])
    setCourierNextCursor(null)
    setCursorStack([null])
    setPageIndex(0)
    setConnected(false)
    setError('')
    if (!tokenDraft.trim()) {
      setError('توکن معتبر عملیات را وارد کنید.')
      return
    }
    // State updates are asynchronous; use the submitted values for the first request.
    void requestWithCredentials(tokenDraft.trim(), null, [null])
  }

  async function requestWithCredentials(
    submittedToken: string,
    cursor: string | null,
    stack: Array<string | null>,
  ) {
    setLoading(true)
    const params = new URLSearchParams({ page_size: '25', sort_by: sortBy })
    if (query.trim()) params.set('query', query.trim())
    if (customerId.trim()) params.set('customer_id', customerId.trim())
    if (hasOffer) params.set('has_offer', hasOffer)
    if (deliveryWindow) params.set('delivery_window_state', deliveryWindow)
    if (createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/dispatch/queue?${params}`, {
        headers: { Authorization: `Bearer ${submittedToken}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as QueuePage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ صف مأموریت با قرارداد API سازگار نیست.')
      setItems(page.items)
      setNextCursor(page.nextCursor || null)
      setCursorStack(stack)
      setPageIndex(stack.length - 1)
      setConnected(true)
      setUpdatedAt(new Date().toISOString())
      setError('')
    } catch (cause) {
      setConnected(false)
      setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.')
    } finally {
      setLoading(false)
    }
  }

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (viewMode === 'dispatch') void fetchPage(null, [null])
    else if (viewMode === 'missions') void fetchMissionPage(null, [null])
    else void fetchCourierPage(null, [null])
  }

  function goNext() {
    const cursor = viewMode === 'dispatch' ? nextCursor : viewMode === 'missions' ? missionNextCursor : courierNextCursor
    if (!cursor) return
    const nextStack = [...cursorStack, cursor]
    if (viewMode === 'dispatch') void fetchPage(cursor, nextStack)
    else if (viewMode === 'missions') void fetchMissionPage(cursor, nextStack)
    else void fetchCourierPage(cursor, nextStack)
  }

  function goPrevious() {
    if (pageIndex < 1) return
    const previousStack = cursorStack.slice(0, -1)
    const cursor = previousStack[previousStack.length - 1] || null
    if (viewMode === 'dispatch') void fetchPage(cursor, previousStack)
    else if (viewMode === 'missions') void fetchMissionPage(cursor, previousStack)
    else void fetchCourierPage(cursor, previousStack)
  }

  function switchView(mode: 'dispatch' | 'missions' | 'couriers') {
    setViewMode(mode)
    setCursorStack([null])
    setPageIndex(0)
    setError('')
    if (mode === 'dispatch') void fetchPage(null, [null])
    else if (mode === 'missions') void fetchMissionPage(null, [null])
    else void fetchCourierPage(null, [null])
  }

  async function showCourierDetails(courierId: string) {
    const requestId = ++detailRequestId.current
    setTimelineTitle(courierId); setCourierDetail(null); setCourierAudit([]); setDetailErrors([]); setTimelineLoading(true)
    const requests = await Promise.allSettled([
      fetch(`/operations/v1/couriers/${encodeURIComponent(courierId)}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch('/operations/v1/courier-read-audit?page_size=100', { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
    ])
    const errors: string[] = []
    const read = async <T,>(result: PromiseSettledResult<Response>, label: string): Promise<T | null> => {
      if (result.status === 'rejected') { errors.push(`${label}: خطای ارتباط`); return null }
      if (!result.value.ok) { errors.push(`${label}: ${await readError(result.value)}`); return null }
      try { return await result.value.json() as T } catch { errors.push(`${label}: قالب پاسخ معتبر نیست.`); return null }
    }
    const [detail, audit] = await Promise.all([read<CourierDetail>(requests[0], 'جزئیات پیک'), read<CourierReadAudit>(requests[1], 'گزارش دسترسی')])
    if (requestId !== detailRequestId.current) return
    setCourierDetail(detail); setCourierAudit(Array.isArray(audit?.items) ? audit.items.filter((row) => row.courier_id === courierId) : []); setDetailErrors(errors); setTimelineLoading(false)
  }

  async function showMissionDetails(missionId: string) {
    const requestId = ++detailRequestId.current
    setTimelineTitle(missionId)
    setMissionDetail(null)
    setCourierDetail(null)
    setCourierAudit([])
    setCourierDetail(null)
    setCourierAudit([])
    setMissionTimeline([])
    setMissionAudit([])
    setDetailErrors([])
    setTimelineLoading(true)
    const missionPath = `/operations/v1/missions/${encodeURIComponent(missionId)}`
    const requests = await Promise.allSettled([
      fetch(missionPath, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch(`${missionPath}/timeline`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch(`/operations/v1/mission-read-audit?page_size=25&mission_id=${encodeURIComponent(missionId)}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store',
      }),
    ])
    const errors: string[] = []
    const readResponse = async <T,>(result: PromiseSettledResult<Response>, label: string): Promise<T | null> => {
      if (result.status === 'rejected') {
        errors.push(`${label}: ${result.reason instanceof Error ? result.reason.message : 'خطای ارتباط'}`)
        return null
      }
      if (!result.value.ok) {
        errors.push(`${label}: ${await readError(result.value)}`)
        return null
      }
      try { return await result.value.json() as T }
      catch { errors.push(`${label}: قالب پاسخ معتبر نیست.`); return null }
    }
    const [detail, timelineResult, auditResult] = await Promise.all([
      readResponse<MissionRecord>(requests[0], 'جزئیات مأموریت'),
      readResponse<MissionTimeline>(requests[1], 'timeline'),
      readResponse<MissionReadAudit>(requests[2], 'گزارش دسترسی'),
    ])
    if (requestId !== detailRequestId.current) return
    setMissionDetail(detail)
    setMissionTimeline(Array.isArray(timelineResult?.events) ? timelineResult.events : [])
    setMissionAudit(Array.isArray(auditResult?.items) ? auditResult.items : [])
    setDetailErrors(errors)
    setTimelineLoading(false)
  }

  function closeDetails() {
    detailRequestId.current += 1
    setTimelineTitle('')
    setTimelineLoading(false)
  }

  function disconnect() {
    setToken('')
    setTokenDraft('')
    setItems([])
    setMissionItems([])
    setCourierItems([])
    setCourierNextCursor(null)
    setNextCursor(null)
    setCursorStack([null])
    setPageIndex(0)
    setConnected(false)
    setMissionDetail(null)
    setCourierDetail(null)
    setCourierAudit([])
    setMissionTimeline([])
    setMissionAudit([])
    closeDetails()
    setError('')
  }

  return (
    <div className="app-shell" dir="rtl">
      <aside className="sidebar">
        <div className="brand-lockup"><span className="brand-mark">م</span><span><strong>مونو</strong><small>LOGISTICS</small></span></div>
        <div className="side-caption">فضای عملیات</div>
        <nav className="navigation" aria-label="ناوبری اصلی">
          <button className={`nav-item ${viewMode === 'dispatch' ? 'active' : ''}`} onClick={() => switchView('dispatch')}><span className="nav-icon">▦</span>صف dispatch</button>
          <button className={`nav-item ${viewMode === 'missions' ? 'active' : ''}`} onClick={() => switchView('missions')}><span className="nav-icon">↗</span>همهٔ مأموریت‌ها</button>
          <button className={`nav-item ${viewMode === 'couriers' ? 'active' : ''}`} onClick={() => switchView('couriers')}><span className="nav-icon">⌁</span>پیک‌ها</button>
          <button className="nav-item" disabled title="این بخش در بسته‌های بعدی فعال می‌شود"><span className="nav-icon">◉</span>زون‌ها و ظرفیت</button>
          <button className="nav-item" disabled title="این بخش در بسته‌های بعدی فعال می‌شود"><span className="nav-icon">▤</span>مالی</button>
        </nav>
        <div className="sidebar-footer"><span className="online-dot" />نسخهٔ عملیاتی داخلی</div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div className="topbar-actions">
            <button className="icon-button" onClick={() => viewMode === 'dispatch' ? void fetchPage(cursorStack[pageIndex] || null) : viewMode === 'missions' ? void fetchMissionPage(cursorStack[pageIndex] || null) : void fetchCourierPage(cursorStack[pageIndex] || null)} disabled={!token || loading} aria-label="تازه‌سازی">↻</button>
            <span className={`system-pill ${connected ? 'is-online' : ''}`}><i />{connected ? 'اتصال به API برقرار است' : 'اتصال برقرار نیست'}</span>
            {token && <button className="text-button" onClick={disconnect}>خروج</button>}
          </div>
          <div className="page-heading"><h1>مرکز کنترل عملیات</h1><p>{viewMode === 'dispatch' ? 'صف مأموریت‌های در انتظار تخصیص پیک' : viewMode === 'missions' ? 'جست‌وجو و پیگیری مأموریت‌های ثبت‌شده' : 'فهرست و سوابق اعتماد پیک‌ها'}</p></div>
        </header>

        {!token && <section className="connect-card">
          <div><span className="eyebrow">دسترسی امن</span><h2>اتصال به API عملیات</h2><p>توکن معتبر مدیر عملیات را وارد کنید. توکن فقط در حافظهٔ همین صفحه نگهداری می‌شود.</p></div>
          <form className="connect-form" onSubmit={connect}>
            <label>توکن Bearer <input type="password" value={tokenDraft} onChange={(event) => setTokenDraft(event.target.value)} placeholder="توکن دسترسی عملیات" dir="ltr" autoComplete="off" /></label>
            <button className="primary-button" disabled={loading}>اتصال و دریافت داده</button>
          </form>
        </section>}

        {error && <div className="error-banner" role="alert"><span>!</span><div><strong>دریافت اطلاعات ناموفق بود</strong><p>{error}</p></div><button onClick={() => setError('')} aria-label="بستن">×</button></div>}

        {viewMode === 'dispatch' && <section className="kpi-grid" aria-label="خلاصهٔ صفحهٔ جاری">
          <article className="kpi-card"><span className="kpi-icon cyan">▦</span><div><small>مأموریت‌های این صفحه</small><strong>{fa.format(totals.rows)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon blue">↗</span><div><small>پیشنهاد فعال در این صفحه</small><strong>{fa.format(totals.offers)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon amber">◷</span><div><small>موعد تحویل گذشته</small><strong>{fa.format(totals.overdue)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon red">!</span><div><small>پیشنهاد با مهلت گذشته</small><strong>{fa.format(totals.lateOffers)}</strong></div></article>
        </section>}

        <section className="queue-panel">
          <div className="panel-heading"><div><span className="eyebrow">{viewMode === 'dispatch' ? 'مدیریت تخصیص' : 'پایش عملیات'}</span><h2>{viewMode === 'dispatch' ? 'صف انتظار dispatch' : viewMode === 'missions' ? 'همهٔ مأموریت‌ها' : 'فهرست پیک‌ها'}</h2></div><div className="panel-meta">{updatedAt ? `به‌روزرسانی ${formatDate(updatedAt)}` : 'دادهٔ زنده از API'}</div></div>
          {viewMode === 'dispatch' ? <form className="filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="جست‌وجوی شناسهٔ مأموریت" /></label>
            <label><span className="sr-only">وضعیت موعد</span><select value={deliveryWindow} onChange={(event) => setDeliveryWindow(event.target.value)}><option value="">همهٔ موعدها</option><option value="upcoming">در پیش</option><option value="due_now">موعد همین حالا</option><option value="overdue">از موعد گذشته</option></select></label>
            <label><span className="sr-only">پیشنهاد فعال</span><select value={hasOffer} onChange={(event) => setHasOffer(event.target.value)}><option value="">همهٔ پیشنهادها</option><option value="true">دارای پیشنهاد فعال</option><option value="false">بدون پیشنهاد فعال</option></select></label>
            <label><span className="sr-only">ترتیب</span><select value={sortBy} onChange={(event) => setSortBy(event.target.value)}><option value="delivery_due">نزدیک‌ترین موعد</option><option value="created_at">جدیدترین مأموریت</option></select></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
          </form> : viewMode === 'missions' ? <form className="filters mission-filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="شناسه مأموریت، درخواست یا مرجع سفارش" /></label>
            <label>از تاریخ<input type="datetime-local" value={createdAfter} onChange={(event) => setCreatedAfter(event.target.value)} /></label>
            <label>تا تاریخ<input type="datetime-local" value={createdBefore} onChange={(event) => setCreatedBefore(event.target.value)} /></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>جست‌وجو</button>
          </form> : <div className="courier-list-note">جست‌وجو در API فهرست پیک‌ها ارائه نشده است؛ این فهرست با صفحه‌بندی API دریافت می‌شود.</div>}
          {viewMode === 'dispatch' && <details className="advanced-filters"><summary>فیلترهای بیشتر</summary><div className="advanced-grid">
            <label>شناسهٔ مشتری<input value={customerId} onChange={(event) => setCustomerId(event.target.value)} placeholder="cus_…" dir="ltr" /></label>
            <label>ایجادشده تا تاریخ<input type="datetime-local" value={createdBefore} onChange={(event) => setCreatedBefore(event.target.value)} /></label>
          </div></details>}

          <div className="table-wrap">
            <table>
              <thead>{viewMode === 'dispatch' ? <tr><th>شناسه مأموریت</th><th>مشتری / شعبه</th><th>زمان ایجاد</th><th>موعد تحویل</th><th>تلاش / آخرین اقدام</th><th>پیشنهاد فعال</th><th>سوابق</th></tr> : viewMode === 'missions' ? <tr><th>شناسه مأموریت</th><th>مشتری</th><th>درخواست</th><th>وضعیت</th><th>موعد تحویل</th><th>زمان ایجاد</th><th>سوابق</th></tr> : <tr><th>شناسه پیک</th><th>نوع عملیات</th><th>وضعیت اعتماد</th><th>زون‌های ترجیحی</th><th>زون‌های تأییدشده</th><th>زون اصلی</th><th>آخرین تغییر</th><th>جزئیات</th></tr>}</thead>
              <tbody>
                {loading && <tr><td colSpan={8} className="state-cell"><span className="spinner" />در حال دریافت اطلاعات…</td></tr>}
                {!loading && (viewMode === 'dispatch' ? items.length === 0 : viewMode === 'missions' ? missionItems.length === 0 : courierItems.length === 0) && <tr><td colSpan={8} className="state-cell"><span className="empty-icon">⌕</span><strong>{token ? 'رکوردی در این صفحه پیدا نشد' : 'برای شروع، به API متصل شوید'}</strong><small>{token ? 'صفحه را تازه کنید یا به صفحهٔ قبل برگردید.' : 'هیچ دادهٔ نمونه‌ای در این صفحه نمایش داده نمی‌شود.'}</small></td></tr>}
                {!loading && viewMode === 'dispatch' && items.map((mission) => <tr key={mission.missionId}>
                  <td><strong className="mission-id" dir="ltr">{mission.missionId}</strong>{mission.deliveryOverdue && <span className="badge danger">موعد گذشته</span>}</td>
                  <td><span className="cell-primary" dir="ltr">{mission.customerId}</span><span className="cell-secondary" dir="ltr">{mission.branchId}</span></td>
                  <td>{formatDate(mission.createdAt)}</td>
                  <td><span className="cell-primary">{formatDate(mission.committedDeliveryAt)}</span><span className={`badge ${mission.deliveryWindowState === 'overdue' ? 'danger' : mission.deliveryWindowState === 'due_now' ? 'warning' : 'neutral'}`}>{statusLabel(mission.deliveryWindowState)}</span></td>
                  <td><span className="cell-primary">{fa.format(mission.attemptCount)} تلاش</span><span className="cell-secondary">{mission.latestAttempt?.action || mission.latestAttempt?.outcome || 'بدون تلاش ثبت‌شده'}</span></td>
                  <td>{mission.activeOffer ? <><span className="cell-primary" dir="ltr">{mission.activeOffer.courierId || mission.activeOffer.offerId || 'پیشنهاد فعال'}</span><span className={`badge ${mission.activeOffer.deadlinePassed ? 'danger' : 'success'}`}>{mission.activeOffer.deadlinePassed ? 'مهلت پاسخ گذشته' : `مهلت ${formatDate(mission.activeOffer.expiresAt)}`}</span></> : <span className="cell-secondary">پیشنهادی فعال ندارد</span>}</td>
                  <td><button className="history-button" onClick={() => void showMissionDetails(mission.missionId)}>جزئیات</button></td>
                </tr>)}
                {!loading && viewMode === 'missions' && missionItems.map((mission) => <tr key={mission.missionId}>
                  <td><strong className="mission-id" dir="ltr">{mission.missionId}</strong></td>
                  <td><span className="cell-primary" dir="ltr">{mission.customerId}</span></td>
                  <td><span className="cell-primary" dir="ltr">{mission.deliveryRequestId}</span></td>
                  <td><span className="badge neutral">{mission.status}</span></td>
                  <td>{formatDate(mission.committedDeliveryAt)}</td>
                  <td>{formatDate(mission.createdAt)}</td>
                  <td><button className="history-button" onClick={() => void showMissionDetails(mission.missionId)}>جزئیات</button></td>
                </tr>)}
                {!loading && viewMode === 'couriers' && courierItems.map((courier) => <tr key={courier.courier_id}>
                  <td><strong className="mission-id" dir="ltr">{courier.courier_id}</strong></td>
                  <td><span className="cell-primary" dir="ltr">{courier.mode}</span></td>
                  <td><span className={`badge ${courier.status === 'ACTIVE' ? 'success' : 'neutral'}`}>{courier.status}</span></td>
                  <td dir="ltr">{courier.preferred_zone_ids.join('، ') || '—'}</td><td dir="ltr">{courier.approved_zone_ids.join('، ') || '—'}</td>
                  <td dir="ltr">{courier.primary_zone_id || '—'}</td><td>{formatDate(courier.updated_at)}</td>
                  <td><button className="history-button" onClick={() => void showCourierDetails(courier.courier_id)}>جزئیات</button></td>
                </tr>)}
              </tbody>
            </table>
          </div>
          <footer className="table-footer"><span>{fa.format(viewMode === 'dispatch' ? items.length : viewMode === 'missions' ? missionItems.length : courierItems.length)} ردیف در این صفحه</span><div className="pagination"><button onClick={goPrevious} disabled={!token || loading || pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(pageIndex + 1)}</span><button onClick={goNext} disabled={!token || loading || !(viewMode === 'dispatch' ? nextCursor : viewMode === 'missions' ? missionNextCursor : courierNextCursor)}>بعدی</button></div></footer>
        </section>
        {viewMode === 'dispatch' && <p className="data-note">آمار بالا فقط از ردیف‌های صفحهٔ جاری محاسبه شده است؛ API فعلاً شمار کل صف را برنمی‌گرداند.</p>}
      </main>

      {timelineTitle && <div className="modal-backdrop" role="presentation" onClick={closeDetails}><section className="timeline-modal detail-modal" role="dialog" aria-modal="true" aria-labelledby="timeline-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">جزئیات و سوابق عملیاتی</span><h2 id="timeline-title" dir="ltr">{timelineTitle}</h2></div><button className="icon-button" onClick={closeDetails} aria-label="بستن">×</button></header>{timelineLoading ? <p className="modal-state">در حال دریافت اطلاعات…</p> : viewMode === 'couriers' ? <>
        {detailErrors.length > 0 && <div className="detail-errors">{detailErrors.map((message) => <p key={message}>{message}</p>)}</div>}
        <section className="detail-section"><h3>مشخصات پیک</h3>{courierDetail ? <dl className="detail-grid">
          <div><dt>شناسه پیک</dt><dd dir="ltr">{courierDetail.courier_id}</dd></div><div><dt>نوع عملیات</dt><dd dir="ltr">{courierDetail.mode}</dd></div><div><dt>وضعیت اعتماد</dt><dd>{courierDetail.status}</dd></div>
          <div><dt>زون اصلی</dt><dd dir="ltr">{courierDetail.primary_zone_id || '—'}</dd></div><div><dt>زون‌های ترجیحی</dt><dd dir="ltr">{courierDetail.preferred_zone_ids.join('، ') || '—'}</dd></div><div><dt>زون‌های تأییدشده</dt><dd dir="ltr">{courierDetail.approved_zone_ids.join('، ') || '—'}</dd></div>
          <div><dt>ثبت‌کننده</dt><dd dir="ltr">{courierDetail.created_by}</dd></div><div><dt>زمان ثبت</dt><dd>{formatDate(courierDetail.created_at)}</dd></div><div><dt>آخرین تغییر</dt><dd>{formatDate(courierDetail.updated_at)}</dd></div>
        </dl> : <p className="modal-state">جزئیات پیک در دسترس نیست.</p>}</section>
        <section className="detail-section"><h3>چرخهٔ اعتماد</h3>{courierDetail?.trust_transitions.length ? <ol className="event-list">{courierDetail.trust_transitions.map((event) => <li key={event.transition_id}><span className="event-dot"/><div><strong>{event.from_status} ← {event.to_status}</strong><small>{formatDate(event.occurred_at)} · {event.actor_id} · {event.reason_code}</small></div></li>)}</ol> : <p className="modal-state">انتقال اعتمادی ثبت نشده است.</p>}</section>
        <section className="detail-section"><h3>سوابق خواندن پیک</h3>{courierAudit.length ? <div className="audit-list">{courierAudit.map((record) => <article key={record.audit_id}><div><strong>{record.action}</strong><small>{record.actor_id}</small></div><span>{formatDate(record.occurred_at)}</span><em>{fa.format(record.result_count)} نتیجه</em></article>)}</div> : <p className="modal-state">در ۱۰۰ رویداد اخیر، سابقه‌ای برای این پیک پیدا نشد.</p>}</section>
      </> : <>
        {detailErrors.length > 0 && <div className="detail-errors">{detailErrors.map((message) => <p key={message}>{message}</p>)}</div>}
        <section className="detail-section"><h3>مشخصات مأموریت</h3>{missionDetail ? <dl className="detail-grid">
          <div><dt>وضعیت</dt><dd><span className="badge neutral">{missionDetail.status}</span></dd></div>
          <div><dt>مشتری</dt><dd dir="ltr">{missionDetail.customerId}</dd></div>
          <div><dt>درخواست تحویل</dt><dd dir="ltr">{missionDetail.deliveryRequestId}</dd></div>
          <div><dt>ایجاد</dt><dd>{formatDate(missionDetail.createdAt)}</dd></div>
          <div><dt>موعد تحویل متعهدشده</dt><dd>{formatDate(missionDetail.committedDeliveryAt)}</dd></div>
        </dl> : <p className="modal-state">جزئیات مأموریت در دسترس نیست.</p>}</section>
        <section className="detail-section"><h3>رویدادهای مأموریت</h3>{missionTimeline.length ? <ol className="event-list">{missionTimeline.map((event) => <li key={event.eventId}><span className="event-dot"/><div><strong>{event.eventType}</strong><small>{formatDate(event.occurredAt)} · {fa.format(event.sequence)}</small></div></li>)}</ol> : <p className="modal-state">رویدادی در timeline ثبت نشده است.</p>}</section>
        <section className="detail-section"><h3>سوابق دسترسی عملیات</h3>{missionAudit.length ? <div className="audit-list">{missionAudit.map((record) => <article key={record.auditId}><div><strong>{record.action}</strong><small>{record.actorId}</small></div><span>{formatDate(record.occurredAt)}</span><em>{fa.format(record.resultCount)} نتیجه</em></article>)}</div> : <p className="modal-state">سابقه‌ای برای نمایش برنگشت.</p>}</section>
      </>}</section></div>}
    </div>
  )
}
