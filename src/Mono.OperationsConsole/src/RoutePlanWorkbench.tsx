import { useCallback, useEffect, useState } from 'react'
import './route-plan-workbench.css'

type RoutePlan = {
  version: number
  recordedAt: string
  snapshot: {
    routeId: string
    startLocationId: string
    startedAt: string
    missions: Array<{ missionId: string; committedDeliveryAt: string }>
    orderedStops: Array<{
      missionId: string
      kind: number | string
      locationId: string
      serviceSeconds: number
      pickupReadyAt: string | null
    }>
  }
}
type RouteAuditPage = {
  items: Array<{
    audit_id: string
    actor_id: string
    route_id: string | null
    action: string
    version: number | null
    outcome: string
    result_count: number
    occurred_at: string
  }>
  nextCursor: string | null
}
type Props = { token: string; reloadKey: number; onConnected: (connected: boolean) => void }
type ApiError = { error?: { code?: string; message?: string }; error_code?: string }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
const actionLabels: Record<string, string> = {
  ROUTE_PLAN_CURRENT_READ: 'مشاهدهٔ نسخهٔ جاری',
  ROUTE_PLAN_VERSION_READ: 'مشاهدهٔ نسخه',
  ROUTE_PLAN_AUDIT_INSPECTION: 'بررسی سابقه',
}

function formatDate(value: string | null | undefined) {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.valueOf()) ? '—' : dateTime.format(parsed)
}

async function errorMessage(response: Response) {
  try {
    const body = await response.json() as ApiError
    return body.error?.message || body.error?.code || body.error_code || `خطای سرور (${response.status})`
  } catch {
    return `خطای سرور (${response.status})`
  }
}

export default function RoutePlanWorkbench({ token, reloadKey, onConnected }: Props) {
  const [routeIdDraft, setRouteIdDraft] = useState('')
  const [routeId, setRouteId] = useState('')
  const [plan, setPlan] = useState<RoutePlan | null>(null)
  const [audit, setAudit] = useState<RouteAuditPage | null>(null)
  const [auditCursor, setAuditCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [auditError, setAuditError] = useState('')

  const load = useCallback(async (id: string, cursor?: string | null) => {
    if (!token || !id) return
    setLoading(true)
    setError('')
    setAuditError('')
    if (!cursor) { setPlan(null); setAudit(null); setAuditCursor(null) }
    let planSucceeded = false
    try {
      if (!cursor) {
        const response = await fetch(`/operations/v1/routes/${encodeURIComponent(id)}/plan`, {
          headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store',
        })
        if (!response.ok) throw new Error(await errorMessage(response))
        const result = await response.json() as RoutePlan
        if (!result.snapshot || result.snapshot.routeId !== id || !Array.isArray(result.snapshot.orderedStops) || !Array.isArray(result.snapshot.missions)) {
          throw new Error('ساختار برنامهٔ مسیر با قرارداد API سازگار نیست.')
        }
        setPlan(result)
        planSucceeded = true
      }

      const now = new Date()
      const from = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000)
      const query = new URLSearchParams({
        route_id: id,
        from_at: from.toISOString(),
        to_at: now.toISOString(),
        limit: '50',
      })
      if (cursor) query.set('cursor', cursor)
      const auditResponse = await fetch(`/operations/v1/routes/plan-read-audit?${query}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store',
      })
      if (!auditResponse.ok) throw new Error(await errorMessage(auditResponse))
      const auditPage = await auditResponse.json() as RouteAuditPage
      if (!Array.isArray(auditPage.items)) throw new Error('ساختار سابقهٔ مسیر با قرارداد API سازگار نیست.')
      setAudit(auditPage)
      setAuditCursor(auditPage.nextCursor || null)
      onConnected(true)
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'دریافت اطلاعات مسیر ناموفق بود.'
      if (cursor || planSucceeded) { setAuditError(message); onConnected(true) }
      else { setError(message); setPlan(null); setAudit(null); onConnected(false) }
    } finally {
      setLoading(false)
    }
  }, [onConnected, token])

  useEffect(() => {
    if (routeId) void load(routeId)
  }, [load, reloadKey, routeId])

  function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const id = routeIdDraft.trim()
    if (!id || id.length > 128) {
      setError('شناسهٔ مسیر را با حداکثر ۱۲۸ نویسه وارد کنید.')
      return
    }
    setRouteId(id)
  }

  const stops = plan?.snapshot.orderedStops || []
  const missionDeadlines = new Map((plan?.snapshot.missions || []).map((mission) => [mission.missionId, mission.committedDeliveryAt]))

  return <main className="route-plan-workbench">
    <section className="route-plan-search queue-panel">
      <div className="panel-heading"><div><span className="eyebrow">برنامه‌ریزی مسیر</span><h2>جست‌وجوی برنامهٔ ثبت‌شده</h2></div><span className="panel-meta">فقط خواندن</span></div>
      <form className="route-plan-search-form" onSubmit={submit}>
        <label htmlFor="route-plan-id">شناسهٔ مسیر</label>
        <input id="route-plan-id" value={routeIdDraft} onChange={(event) => setRouteIdDraft(event.target.value)} dir="ltr" maxLength={128} placeholder="route_…" autoComplete="off" />
        <button className="primary-button" disabled={!token || loading || !routeIdDraft.trim()}>{loading ? 'در حال دریافت…' : 'جست‌وجوی مسیر'}</button>
      </form>
      {!token && <p className="route-plan-notice">برای مشاهدهٔ برنامهٔ مسیر با توکن مدیر عملیات متصل شوید.</p>}
      <p className="route-plan-notice">API فعلی فهرست همهٔ مسیرها، نقشه، موقعیت لحظه‌ای پیک، ETA یا شاخص‌های شبکه را ارائه نمی‌کند؛ این پنل فقط برنامهٔ ثبت‌شده برای شناسهٔ واردشده را نمایش می‌دهد.</p>
      {error && <div className="onboarding-error" role="alert">{error}</div>}
    </section>

    {plan && <>
      <section className="route-plan-summary queue-panel">
        <div className="panel-heading"><div><span className="eyebrow">مسیر {plan.snapshot.routeId}</span><h2>نسخهٔ {fa.format(plan.version)}</h2></div><span className="panel-meta">ثبت‌شده در {formatDate(plan.recordedAt)}</span></div>
        <dl className="route-plan-facts">
          <div><dt>نقطهٔ شروع</dt><dd dir="ltr">{plan.snapshot.startLocationId}</dd></div>
          <div><dt>زمان شروع</dt><dd>{formatDate(plan.snapshot.startedAt)}</dd></div>
          <div><dt>مأموریت‌های مسیر</dt><dd>{fa.format(plan.snapshot.missions.length)}</dd></div>
          <div><dt>توقف‌های ثبت‌شده</dt><dd>{fa.format(stops.length)}</dd></div>
        </dl>
      </section>

      <section className="route-plan-stops queue-panel">
        <div className="panel-heading"><div><span className="eyebrow">ترتیب توقف‌ها</span><h2>برنامهٔ ذخیره‌شده</h2></div><span className="panel-meta">نسخهٔ جاری مسیر</span></div>
        <div className="table-wrap"><table><thead><tr><th>ترتیب</th><th>نوع توقف</th><th>مأموریت</th><th>شناسهٔ مکان</th><th>زمان آماده‌بودن دریافت</th><th>زمان تعهد تحویل</th><th>زمان خدمت</th></tr></thead><tbody>
          {stops.map((stop, index) => <tr key={`${index}-${stop.missionId}-${stop.locationId}`}>
            <td>{fa.format(index + 1)}</td><td>{stop.kind === 0 || stop.kind === 'Pickup' ? 'دریافت' : 'تحویل'}</td>
            <td dir="ltr">{stop.missionId}</td><td dir="ltr">{stop.locationId}</td>
            <td>{formatDate(stop.pickupReadyAt)}</td><td>{formatDate(missionDeadlines.get(stop.missionId))}</td><td>{fa.format(stop.serviceSeconds)} ثانیه</td>
          </tr>)}
          {stops.length === 0 && <tr><td colSpan={7} className="state-cell">برای این نسخه توقفی ثبت نشده است.</td></tr>}
        </tbody></table></div>
      </section>

      <section className="route-plan-audit queue-panel">
        <div className="panel-heading"><div><span className="eyebrow">ممیزی دسترسی مسیر</span><h2>تاریخچهٔ مشاهده</h2></div><button className="secondary-button" onClick={() => void load(plan.snapshot.routeId)} disabled={loading}>تازه‌سازی</button></div>
        {auditError && <div className="onboarding-error" role="alert">{auditError}</div>}
        <div className="table-wrap"><table><thead><tr><th>زمان</th><th>عامل</th><th>عملیات</th><th>نسخه</th><th>نتیجه</th><th>رکوردها</th></tr></thead><tbody>
          {audit?.items.map((item) => <tr key={item.audit_id}><td>{formatDate(item.occurred_at)}</td><td dir="ltr">{item.actor_id}</td><td>{actionLabels[item.action] || item.action}</td><td>{item.version ? fa.format(item.version) : '—'}</td><td dir="ltr">{item.outcome}</td><td>{fa.format(item.result_count)}</td></tr>)}
          {(!audit || audit.items.length === 0) && <tr><td colSpan={6} className="state-cell">در ۳۰ روز گذشته سابقه‌ای برای این مسیر پیدا نشد.</td></tr>}
        </tbody></table></div>
        {auditCursor && <div className="route-plan-audit-footer"><span>۵۰ رویداد اخیر نمایش داده شده</span><button className="secondary-button" onClick={() => void load(plan.snapshot.routeId, auditCursor)} disabled={loading}>رویدادهای قدیمی‌تر</button></div>}
      </section>
    </>}
  </main>
}
