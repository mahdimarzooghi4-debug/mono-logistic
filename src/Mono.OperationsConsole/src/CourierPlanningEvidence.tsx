import { useCallback, useEffect, useRef, useState } from 'react'
import './courier-planning-evidence.css'

type PlanningItem = {
  courier_id: string
  mode: string
  trust_status: string
  operational_status: string
  zone_approved: boolean
  has_scheduled_window: boolean
  planned_capacity_contributor: boolean
  exclusion_reasons: string[]
  is_online: boolean
  live_presence_zone_id: string | null
  live_presence_updated_at: string | null
  live_presence_candidate: boolean
  live_presence_exclusion_reasons: string[]
}

type PlanningPage = {
  zone_id: string
  from: string
  to: string
  classification: string
  items: PlanningItem[]
  next_cursor: string | null
}

type Search = { zoneId: string; from: string; to: string }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })

function formatDate(value: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date)
}

function reasonLabel(reason: string) {
  const labels: Record<string, string> = {
    COURIER_NOT_ACTIVE: 'پیک فعال نیست',
    COURIER_SUSPENDED: 'تعلیق عملیاتی',
    ZONE_NOT_APPROVED: 'زون تأیید نشده',
    NO_SCHEDULED_WINDOW: 'بدون برنامهٔ کاری در بازه',
    COURIER_NOT_ONLINE: 'حضور آنلاین گزارش نشده',
    ONLINE_IN_DIFFERENT_ZONE: 'آنلاین در زون دیگر',
  }
  return labels[reason] || reason
}

async function errorMessage(response: Response) {
  try {
    const body = await response.json() as { error?: { message?: string }; message?: string }
    return body.error?.message || body.message || 'دریافت شواهد پیک‌ها ناموفق بود.'
  } catch {
    return 'دریافت شواهد پیک‌ها ناموفق بود.'
  }
}

export default function CourierPlanningEvidence({
  token,
  zoneId,
  fromLocal,
  toLocal,
  refreshKey,
}: {
  token: string
  zoneId: string
  fromLocal: string
  toLocal: string
  refreshKey: number
}) {
  const [search, setSearch] = useState<Search | null>(null)
  const [page, setPage] = useState<PlanningPage | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const requestSequence = useRef(0)
  const controller = useRef<AbortController | null>(null)
  const completedRefresh = useRef(0)

  const loadPage = useCallback(async (query: Search, cursor: string | null, stack: Array<string | null>) => {
    controller.current?.abort()
    const activeController = new AbortController()
    controller.current = activeController
    const requestId = ++requestSequence.current
    const params = new URLSearchParams({ zone_id: query.zoneId, from: query.from, to: query.to, page_size: '50' })
    if (cursor) params.set('cursor', cursor)
    setLoading(true)
    setError('')
    try {
      const response = await fetch('/operations/v1/courier-planning-eligibility?' + params.toString(), {
        headers: { Authorization: 'Bearer ' + token, Accept: 'application/json' },
        cache: 'no-store',
        signal: activeController.signal,
      })
      if (!response.ok) throw new Error(await errorMessage(response))
      const result = await response.json() as PlanningPage
      if (!Array.isArray(result.items) || result.classification !== 'PLANNED_CAPACITY_AND_LIVE_PRESENCE_SIGNALS')
        throw new Error('ساختار پاسخ شواهد برنامه‌ریزی و حضور با قرارداد API سازگار نیست.')
      if (requestId !== requestSequence.current || activeController.signal.aborted) return
      setPage(result)
      setNextCursor(result.next_cursor || null)
      setCursorStack(stack)
    } catch (cause) {
      if (activeController.signal.aborted || requestId !== requestSequence.current) return
      setPage(null)
      setNextCursor(null)
      setError(cause instanceof Error ? cause.message : 'دریافت شواهد پیک‌ها ناموفق بود.')
    } finally {
      if (requestId === requestSequence.current && !activeController.signal.aborted) setLoading(false)
    }
  }, [token])

  useEffect(() => {
    if (!token) {
      controller.current?.abort()
      setSearch(null)
      setPage(null)
      setNextCursor(null)
      setCursorStack([null])
      setLoading(false)
      setError('')
      completedRefresh.current = 0
      return
    }
    if (!refreshKey || completedRefresh.current === refreshKey) return
    completedRefresh.current = refreshKey
    const from = new Date(fromLocal)
    const to = new Date(toLocal)
    if (!zoneId.trim() || Number.isNaN(from.valueOf()) || Number.isNaN(to.valueOf()) || to <= from || to.getTime() - from.getTime() > 24 * 60 * 60 * 1000) {
      setError('برای مشاهدهٔ شواهد، شناسهٔ زون و بازهٔ معتبر تا ۲۴ ساعت را وارد کنید.')
      setPage(null)
      setLoading(false)
      return
    }
    const query = { zoneId: zoneId.trim(), from: from.toISOString(), to: to.toISOString() }
    setSearch(query)
    setCursorStack([null])
    void loadPage(query, null, [null])
  }, [token, refreshKey, zoneId, fromLocal, toLocal, loadPage])

  useEffect(() => () => controller.current?.abort(), [])

  if (!token || !refreshKey) return null

  const counts = {
    planned: page?.items.filter((item) => item.planned_capacity_contributor).length || 0,
    online: page?.items.filter((item) => item.is_online).length || 0,
    candidates: page?.items.filter((item) => item.live_presence_candidate).length || 0,
  }

  return (
    <section className="planning-presence-result" aria-label="شواهد برنامه‌ریزی و حضور پیک‌ها">
      <div className="capacity-result-heading">
        <div><strong>شواهد برنامه‌ریزی و حضور گزارش‌شده</strong><small>زون {search?.zoneId || zoneId || '—'} · بازهٔ انتخاب‌شده · فقط‌خوانی</small></div>
        <span>{page ? 'صفحهٔ ' + fa.format(cursorStack.length) + ' · ' + fa.format(page.items.length) + ' پیک' : loading ? 'در حال دریافت' : 'داده در دسترس نیست'}</span>
      </div>
      {error && <div className="detail-errors capacity-errors" role="alert"><p>{error}</p></div>}
      {loading && <p className="planning-state"><span className="spinner" />در حال دریافت شواهد پیک‌ها…</p>}
      {page && <>
        <div className="planning-summary" aria-label="خلاصهٔ همین صفحه">
          <article><small>سهم در ظرفیت برنامه‌ریزی‌شده</small><strong>{fa.format(counts.planned)}</strong></article>
          <article><small>وضعیت آنلاین گزارش‌شده</small><strong>{fa.format(counts.online)}</strong></article>
          <article><small>آنلاین و تأییدشده در همین زون</small><strong>{fa.format(counts.candidates)}</strong></article>
        </div>
        <div className="planning-table-wrap"><table className="planning-table"><thead><tr><th>پیک</th><th>اعتماد / عملیات</th><th>ظرفیت برنامه‌ریزی‌شده</th><th>حضور گزارش‌شده</th><th>زون حضور</th><th>آخرین تغییر حضور</th><th>دلایل عدم احراز</th></tr></thead><tbody>
          {page.items.map((courier) => <tr key={courier.courier_id}>
            <td><strong className="mission-id" dir="ltr">{courier.courier_id}</strong><span className="cell-secondary" dir="ltr">{courier.mode}</span></td>
            <td><span className={'badge ' + (courier.trust_status === 'ACTIVE' ? 'success' : 'neutral')}>{courier.trust_status}</span><span className="cell-secondary">{courier.operational_status}</span></td>
            <td><span className={'badge ' + (courier.planned_capacity_contributor ? 'success' : 'neutral')}>{courier.planned_capacity_contributor ? 'مشارکت دارد' : 'مشارکت ندارد'}</span>{courier.exclusion_reasons.length > 0 && <span className="cell-secondary">{courier.exclusion_reasons.map(reasonLabel).join(' · ')}</span>}</td>
            <td><span className={'badge ' + (courier.is_online ? 'success' : 'neutral')}>{courier.is_online ? 'آنلاین گزارش‌شده' : 'آفلاین گزارش‌شده'}</span><span className="cell-secondary">{courier.live_presence_candidate ? 'تأییدشده در زون انتخاب‌شده' : 'کاندیدای حضور نیست'}</span></td>
            <td dir="ltr">{courier.live_presence_zone_id || '—'}</td>
            <td>{formatDate(courier.live_presence_updated_at)}</td>
            <td>{courier.live_presence_exclusion_reasons.length ? <span className="cell-secondary">{courier.live_presence_exclusion_reasons.map(reasonLabel).join(' · ')}</span> : <span className="cell-secondary">—</span>}</td>
          </tr>)}
          {!page.items.length && <tr><td colSpan={7} className="state-cell">برای این زون پیکی در نتیجهٔ جست‌وجو نیست.</td></tr>}
        </tbody></table></div>
        <div className="planning-pagination">
          <span>خلاصه‌ها فقط برای همین صفحهٔ نتایج هستند.</span>
          <div>
            <button type="button" onClick={() => { const stack = cursorStack.slice(0, -1); void loadPage(search!, stack.at(-1) || null, stack) }} disabled={loading || cursorStack.length <= 1}>قبلی</button>
            <span>صفحهٔ {fa.format(cursorStack.length)}</span>
            <button type="button" onClick={() => { if (!nextCursor || !search) return; const stack = [...cursorStack, nextCursor]; void loadPage(search, nextCursor, stack) }} disabled={loading || !nextCursor}>بعدی</button>
          </div>
        </div>
        <p className="data-note">حضور بر اساس آخرین وضعیت گزارش‌شدهٔ پیک نمایش داده می‌شود؛ زمان آن را بررسی کنید. این شواهد به‌تنهایی حضور فیزیکی، ظرفیت قابل‌تخصیص یا مجوز اعزام را اثبات نمی‌کند.</p>
      </>}
    </section>
  )
}
