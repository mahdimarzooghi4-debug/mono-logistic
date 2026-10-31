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

  return <main className="route-control-workbench" dir="rtl">
    <header className="route-control-header"><div className="route-control-title"><h2>کنترل مسیرها</h2><p>نمای زندهٔ مسیرهای فعال، ترتیب توقف‌ها، ظرفیت و ریسک استاندارد</p></div><div className="route-control-actions"><button className="secondary-button" disabled title="API فهرست مسیرهای فعال را ارائه نمی‌کند">بهینه‌سازی مسیر</button><button className="primary-button" disabled title="API فعلی ایجاد مسیر را ارائه نمی‌کند">ایجاد مسیر</button></div></header>
    <section className="route-control-kpis" aria-label="خلاصهٔ مسیرها">
      <article><span>مسیرهای فعال</span><strong>—</strong></article>
      <article><span>مأموریت‌های Batched</span><strong>—</strong></article>
      <article><span>مسیرهای در معرض خطر</span><strong>—</strong></article>
      <article><span>میانگین مأموریت در مسیر</span><strong>—</strong></article>
      <article><span>ظرفیت آزاد شبکه</span><strong>—</strong></article>
    </section>
    <div className="route-control-layout">
      <aside className="route-list-panel queue-panel">
        <div className="route-list-heading"><span className="route-count-pill">— فعال</span><h3>مسیرهای فعال</h3></div>
        <form className="route-list-search" onSubmit={submit}>
          <label htmlFor="route-plan-id" className="sr-only">جست‌وجوی مسیر یا پیک</label>
          <input id="route-plan-id" value={routeIdDraft} onChange={(event) => setRouteIdDraft(event.target.value)} dir="auto" maxLength={128} placeholder="جست‌وجوی مسیر یا پیک" autoComplete="off" />
          <button className="primary-button" disabled={!token || loading || !routeIdDraft.trim()}>{loading ? 'در حال دریافت…' : 'جست‌وجو'}</button>
        </form>
        {error && <div className="onboarding-error route-inline-error" role="alert">{error}</div>}
        {!token ? <p className="route-list-empty">برای خواندن مسیرها، ابتدا به API عملیات متصل شوید.</p> : <p className="route-list-empty">فهرست مسیرهای فعال هنوز از API در دسترس نیست. می‌توانید شناسهٔ یک مسیر ثبت‌شده را جست‌وجو کنید.</p>}
        {plan && <button className="route-selected-card" aria-current="true" onClick={() => void load(plan.snapshot.routeId)} disabled={loading}><span className="route-status-pill">نسخهٔ {fa.format(plan.version)}</span><strong dir="ltr">{plan.snapshot.routeId}</strong><small>{fa.format(plan.snapshot.missions.length)} مأموریت · نقطهٔ شروع {plan.snapshot.startLocationId}</small></button>}
      </aside>
      <section className="route-detail-panel queue-panel">
        {plan ? <>
          <section className="route-summary-card"><header className="route-detail-heading"><div><h3 dir="ltr">{plan.snapshot.routeId}</h3><p>پیک: اطلاعات در API نیست · {fa.format(plan.snapshot.missions.length)} مأموریت · {fa.format(stops.length)} توقف</p></div><span className="route-status-pill">وضعیت مسیر: نامشخص</span></header><div className="route-metrics">
            <div><span>پایان تخمینی</span><strong>—</strong></div><div><span>انحراف فعلی</span><strong>—</strong></div><div><span>ظرفیت استفاده‌شده</span><strong>—</strong></div><div><span>ریسک استاندارد</span><strong>—</strong></div>
          </div></section>
          <div className="route-detail-overview">
            <section className="route-map-placeholder" aria-label="نقشهٔ مسیر"><span className="route-map-icon">⌖</span><strong>نقشهٔ مسیر</strong><p>مختصات توقف‌ها و موقعیت لحظه‌ای پیک در API موجود نیست.</p><small>شروع برنامه: {formatDate(plan.snapshot.startedAt)}</small></section>
            <section className="route-optimization-card"><h3>فرصت بهینه‌سازی</h3><span className="route-status-pill muted">در دسترس نیست</span><p>پیشنهاد افزودن مأموریت پس از ارائهٔ دادهٔ مکان و API ارزیابی استاندارد فعال می‌شود.</p><dl><div><dt>مأموریت‌های مسیر</dt><dd>{fa.format(plan.snapshot.missions.length)}</dd></div><div><dt>توقف‌های ثبت‌شده</dt><dd>{fa.format(stops.length)}</dd></div><div><dt>اثر بر زمان مسیر</dt><dd>—</dd></div></dl><button disabled title="API ارزیابی و افزودن مأموریت به مسیر موجود نیست">افزودن به مسیر</button></section>
          </div>
          <section className="route-stop-section"><header><div><h3>ترتیب توقف‌ها</h3><p>نسخهٔ ثبت‌شدهٔ برنامهٔ مسیر</p></div><span className="route-status-pill muted">{fa.format(stops.length)} توقف</span></header><div className="table-wrap"><table><thead><tr><th>ترتیب</th><th>نوع</th><th>مأموریت</th><th>مکان</th><th>ETA</th><th>استاندارد خدمت</th><th>وضعیت</th></tr></thead><tbody>
            {stops.map((stop, index) => <tr key={`${index}-${stop.missionId}-${stop.locationId}`}>
              <td>{fa.format(index + 1)}</td><td>{stop.kind === 0 || stop.kind === 'Pickup' ? 'دریافت' : 'تحویل'}</td><td dir="ltr">{stop.missionId}</td><td dir="ltr">{stop.locationId}</td><td>—</td><td>—</td><td>از API دریافت نمی‌شود</td>
            </tr>)}
            {stops.length === 0 && <tr><td colSpan={7} className="state-cell">برای این نسخه توقفی ثبت نشده است.</td></tr>}
          </tbody></table></div></section>
          <details className="route-audit-disclosure"><summary>سوابق ممیزی دسترسی مسیر</summary>{auditError && <div className="onboarding-error" role="alert">{auditError}</div>}<div className="table-wrap"><table><thead><tr><th>زمان</th><th>عامل</th><th>عملیات</th><th>نسخه</th><th>نتیجه</th><th>رکوردها</th></tr></thead><tbody>
            {audit?.items.map((item) => <tr key={item.audit_id}><td>{formatDate(item.occurred_at)}</td><td dir="ltr">{item.actor_id}</td><td>{actionLabels[item.action] || item.action}</td><td>{item.version ? fa.format(item.version) : '—'}</td><td dir="ltr">{item.outcome}</td><td>{fa.format(item.result_count)}</td></tr>)}
            {(!audit || audit.items.length === 0) && <tr><td colSpan={6} className="state-cell">در ۳۰ روز گذشته سابقه‌ای برای این مسیر پیدا نشد.</td></tr>}
          </tbody></table></div>{auditCursor && <div className="route-plan-audit-footer"><span>۵۰ رویداد اخیر نمایش داده شده</span><button className="secondary-button" onClick={() => void load(plan.snapshot.routeId, auditCursor)} disabled={loading}>رویدادهای قدیمی‌تر</button></div>}</details>
        </> : <div className="route-detail-empty"><span>⌖</span><h3>یک مسیر را جست‌وجو کنید</h3><p>پس از وارد کردن شناسه، اطلاعات برنامهٔ ثبت‌شده و ترتیب توقف‌ها اینجا نمایش داده می‌شود.</p></div>}
      </section>
    </div>
  </main>
}
