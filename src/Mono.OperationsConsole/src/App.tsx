import { useCallback, useMemo, useState } from 'react'
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
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [apiBase, setApiBase] = useState('')
  const [apiBaseDraft, setApiBaseDraft] = useState('')
  const [items, setItems] = useState<Mission[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [pageIndex, setPageIndex] = useState(0)
  const [query, setQuery] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [hasOffer, setHasOffer] = useState('')
  const [deliveryWindow, setDeliveryWindow] = useState('')
  const [createdBefore, setCreatedBefore] = useState('')
  const [sortBy, setSortBy] = useState('delivery_due')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [connected, setConnected] = useState(false)
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [timeline, setTimeline] = useState<unknown[] | null>(null)
  const [timelineTitle, setTimelineTitle] = useState('')
  const [timelineLoading, setTimelineLoading] = useState(false)

  const totals = useMemo(() => ({
    rows: items.length,
    offers: items.filter((item) => item.activeOffer).length,
    overdue: items.filter((item) => item.deliveryOverdue).length,
    lateOffers: items.filter((item) => item.activeOffer?.deadlinePassed).length,
  }), [items])

  const requestUrl = useCallback((path: string) => {
    const base = apiBase.replace(/\/$/, '')
    return `${base}${path}`
  }, [apiBase])

  const fetchPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    const params = new URLSearchParams({ page_size: '25', sort_by: sortBy })
    if (query.trim()) params.set('query', query.trim())
    if (customerId.trim()) params.set('customer_id', customerId.trim())
    if (hasOffer) params.set('has_offer', hasOffer)
    if (deliveryWindow) params.set('delivery_window_state', deliveryWindow)
    if (createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(requestUrl(`/operations/v1/dispatch/queue?${params}`), {
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
  }, [token, sortBy, query, customerId, hasOffer, deliveryWindow, createdBefore, requestUrl])

  function connect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setToken(tokenDraft.trim())
    setTokenDraft('')
    setApiBase(apiBaseDraft.trim())
    setItems([])
    setCursorStack([null])
    setPageIndex(0)
    setConnected(false)
    setError('')
    if (!tokenDraft.trim()) {
      setError('توکن معتبر عملیات را وارد کنید.')
      return
    }
    // State updates are asynchronous; use the submitted values for the first request.
    void requestWithCredentials(tokenDraft.trim(), apiBaseDraft.trim(), null, [null])
  }

  async function requestWithCredentials(
    submittedToken: string,
    submittedBase: string,
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
      const response = await fetch(`${submittedBase.replace(/\/$/, '')}/operations/v1/dispatch/queue?${params}`, {
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
    void fetchPage(null, [null])
  }

  function goNext() {
    if (!nextCursor) return
    const nextStack = [...cursorStack, nextCursor]
    void fetchPage(nextCursor, nextStack)
  }

  function goPrevious() {
    if (pageIndex < 1) return
    const previousStack = cursorStack.slice(0, -1)
    void fetchPage(previousStack[previousStack.length - 1] || null, previousStack)
  }

  async function showTimeline(missionId: string) {
    setTimelineTitle(missionId)
    setTimeline(null)
    setTimelineLoading(true)
    try {
      const response = await fetch(requestUrl(`/operations/v1/missions/${encodeURIComponent(missionId)}/timeline`), {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const result = (await response.json()) as { events?: unknown[] }
      setTimeline(Array.isArray(result.events) ? result.events : [])
    } catch (cause) {
      setTimeline([{ error: cause instanceof Error ? cause.message : 'دریافت سوابق ناموفق بود.' }])
    } finally {
      setTimelineLoading(false)
    }
  }

  function disconnect() {
    setToken('')
    setTokenDraft('')
    setItems([])
    setNextCursor(null)
    setCursorStack([null])
    setPageIndex(0)
    setConnected(false)
    setTimeline(null)
    setError('')
  }

  return (
    <div className="app-shell" dir="rtl">
      <aside className="sidebar">
        <div className="brand-lockup"><span className="brand-mark">م</span><span><strong>مونو</strong><small>LOGISTICS</small></span></div>
        <div className="side-caption">فضای عملیات</div>
        <nav className="navigation" aria-label="ناوبری اصلی">
          <button className="nav-item active"><span className="nav-icon">▦</span>صف dispatch</button>
          <button className="nav-item" disabled title="این بخش در بسته‌های بعدی فعال می‌شود"><span className="nav-icon">↗</span>مأموریت‌ها</button>
          <button className="nav-item" disabled title="این بخش در بسته‌های بعدی فعال می‌شود"><span className="nav-icon">⌁</span>پیک‌ها</button>
          <button className="nav-item" disabled title="این بخش در بسته‌های بعدی فعال می‌شود"><span className="nav-icon">◉</span>زون‌ها و ظرفیت</button>
          <button className="nav-item" disabled title="این بخش در بسته‌های بعدی فعال می‌شود"><span className="nav-icon">▤</span>مالی</button>
        </nav>
        <div className="sidebar-footer"><span className="online-dot" />نسخهٔ عملیاتی داخلی</div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div className="topbar-actions">
            <button className="icon-button" onClick={() => void fetchPage(cursorStack[pageIndex] || null)} disabled={!token || loading} aria-label="تازه‌سازی">↻</button>
            <span className={`system-pill ${connected ? 'is-online' : ''}`}><i />{connected ? 'اتصال به API برقرار است' : 'اتصال برقرار نیست'}</span>
            {token && <button className="text-button" onClick={disconnect}>خروج</button>}
          </div>
          <div className="page-heading"><h1>مرکز کنترل عملیات</h1><p>صف مأموریت‌های در انتظار تخصیص پیک</p></div>
        </header>

        {!token && <section className="connect-card">
          <div><span className="eyebrow">دسترسی امن</span><h2>اتصال به API عملیات</h2><p>توکن معتبر مدیر عملیات را وارد کنید. توکن فقط در حافظهٔ همین صفحه نگهداری می‌شود.</p></div>
          <form className="connect-form" onSubmit={connect}>
            <label>نشانی پایهٔ API <input value={apiBaseDraft} onChange={(event) => setApiBaseDraft(event.target.value)} placeholder="خالی = همین میزبان" dir="ltr" /></label>
            <label>توکن Bearer <input type="password" value={tokenDraft} onChange={(event) => setTokenDraft(event.target.value)} placeholder="توکن دسترسی عملیات" dir="ltr" autoComplete="off" /></label>
            <button className="primary-button" disabled={loading}>اتصال و دریافت صف</button>
          </form>
        </section>}

        {error && <div className="error-banner" role="alert"><span>!</span><div><strong>دریافت اطلاعات ناموفق بود</strong><p>{error}</p></div><button onClick={() => setError('')} aria-label="بستن">×</button></div>}

        <section className="kpi-grid" aria-label="خلاصهٔ صفحهٔ جاری">
          <article className="kpi-card"><span className="kpi-icon cyan">▦</span><div><small>مأموریت‌های این صفحه</small><strong>{fa.format(totals.rows)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon blue">↗</span><div><small>پیشنهاد فعال در این صفحه</small><strong>{fa.format(totals.offers)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon amber">◷</span><div><small>موعد تحویل گذشته</small><strong>{fa.format(totals.overdue)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon red">!</span><div><small>پیشنهاد با مهلت گذشته</small><strong>{fa.format(totals.lateOffers)}</strong></div></article>
        </section>

        <section className="queue-panel">
          <div className="panel-heading"><div><span className="eyebrow">مدیریت تخصیص</span><h2>صف انتظار dispatch</h2></div><div className="panel-meta">{updatedAt ? `به‌روزرسانی ${formatDate(updatedAt)}` : 'دادهٔ زنده از API'}</div></div>
          <form className="filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="جست‌وجوی شناسهٔ مأموریت" /></label>
            <label><span className="sr-only">وضعیت موعد</span><select value={deliveryWindow} onChange={(event) => setDeliveryWindow(event.target.value)}><option value="">همهٔ موعدها</option><option value="upcoming">در پیش</option><option value="due_now">موعد همین حالا</option><option value="overdue">از موعد گذشته</option></select></label>
            <label><span className="sr-only">پیشنهاد فعال</span><select value={hasOffer} onChange={(event) => setHasOffer(event.target.value)}><option value="">همهٔ پیشنهادها</option><option value="true">دارای پیشنهاد فعال</option><option value="false">بدون پیشنهاد فعال</option></select></label>
            <label><span className="sr-only">ترتیب</span><select value={sortBy} onChange={(event) => setSortBy(event.target.value)}><option value="delivery_due">نزدیک‌ترین موعد</option><option value="created_at">جدیدترین مأموریت</option></select></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
          </form>
          <details className="advanced-filters"><summary>فیلترهای بیشتر</summary><div className="advanced-grid">
            <label>شناسهٔ مشتری<input value={customerId} onChange={(event) => setCustomerId(event.target.value)} placeholder="cus_…" dir="ltr" /></label>
            <label>ایجادشده تا تاریخ<input type="datetime-local" value={createdBefore} onChange={(event) => setCreatedBefore(event.target.value)} /></label>
          </div></details>

          <div className="table-wrap">
            <table>
              <thead><tr><th>شناسه مأموریت</th><th>مشتری / شعبه</th><th>زمان ایجاد</th><th>موعد تحویل</th><th>تلاش / آخرین اقدام</th><th>پیشنهاد فعال</th><th>سوابق</th></tr></thead>
              <tbody>
                {loading && <tr><td colSpan={7} className="state-cell"><span className="spinner" />در حال دریافت صف…</td></tr>}
                {!loading && items.length === 0 && <tr><td colSpan={7} className="state-cell"><span className="empty-icon">⌕</span><strong>{token ? 'مأموریتی مطابق فیلترها پیدا نشد' : 'برای شروع، به API متصل شوید'}</strong><small>{token ? 'فیلترها را تغییر دهید یا صف را تازه کنید.' : 'هیچ دادهٔ نمونه‌ای در این صفحه نمایش داده نمی‌شود.'}</small></td></tr>}
                {!loading && items.map((mission) => <tr key={mission.missionId}>
                  <td><strong className="mission-id" dir="ltr">{mission.missionId}</strong>{mission.deliveryOverdue && <span className="badge danger">موعد گذشته</span>}</td>
                  <td><span className="cell-primary" dir="ltr">{mission.customerId}</span><span className="cell-secondary" dir="ltr">{mission.branchId}</span></td>
                  <td>{formatDate(mission.createdAt)}</td>
                  <td><span className="cell-primary">{formatDate(mission.committedDeliveryAt)}</span><span className={`badge ${mission.deliveryWindowState === 'overdue' ? 'danger' : mission.deliveryWindowState === 'due_now' ? 'warning' : 'neutral'}`}>{statusLabel(mission.deliveryWindowState)}</span></td>
                  <td><span className="cell-primary">{fa.format(mission.attemptCount)} تلاش</span><span className="cell-secondary">{mission.latestAttempt?.action || mission.latestAttempt?.outcome || 'بدون تلاش ثبت‌شده'}</span></td>
                  <td>{mission.activeOffer ? <><span className="cell-primary" dir="ltr">{mission.activeOffer.courierId || mission.activeOffer.offerId || 'پیشنهاد فعال'}</span><span className={`badge ${mission.activeOffer.deadlinePassed ? 'danger' : 'success'}`}>{mission.activeOffer.deadlinePassed ? 'مهلت پاسخ گذشته' : `مهلت ${formatDate(mission.activeOffer.expiresAt)}`}</span></> : <span className="cell-secondary">پیشنهادی فعال ندارد</span>}</td>
                  <td><button className="history-button" onClick={() => void showTimeline(mission.missionId)}>مشاهده</button></td>
                </tr>)}
              </tbody>
            </table>
          </div>
          <footer className="table-footer"><span>{fa.format(items.length)} ردیف در این صفحه</span><div className="pagination"><button onClick={goPrevious} disabled={!token || loading || pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(pageIndex + 1)}</span><button onClick={goNext} disabled={!token || loading || !nextCursor}>بعدی</button></div></footer>
        </section>
        <p className="data-note">آمار بالا فقط از ردیف‌های صفحهٔ جاری محاسبه شده است؛ API فعلاً شمار کل صف را برنمی‌گرداند.</p>
      </main>

      {timeline !== null && <div className="modal-backdrop" role="presentation" onClick={() => setTimeline(null)}><section className="timeline-modal" role="dialog" aria-modal="true" aria-labelledby="timeline-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">سوابق ثبت‌شده</span><h2 id="timeline-title" dir="ltr">{timelineTitle}</h2></div><button className="icon-button" onClick={() => setTimeline(null)} aria-label="بستن">×</button></header>{timelineLoading ? <p className="modal-state">در حال دریافت سوابق…</p> : <pre>{JSON.stringify(timeline, null, 2)}</pre>}</section></div>}
    </div>
  )
}
