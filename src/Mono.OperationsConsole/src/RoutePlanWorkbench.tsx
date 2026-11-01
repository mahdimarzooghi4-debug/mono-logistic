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
type Props = { token: string; reloadKey: number; demoMode?: boolean; onConnected: (connected: boolean) => void }
type ApiError = { error?: { code?: string; message?: string }; error_code?: string }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
const actionLabels: Record<string, string> = {
  ROUTE_PLAN_CURRENT_READ: 'مشاهدهٔ نسخهٔ جاری',
  ROUTE_PLAN_VERSION_READ: 'مشاهدهٔ نسخه',
  ROUTE_PLAN_AUDIT_INSPECTION: 'بررسی سابقه',
}
const demoRoutePlan: RoutePlan = { version: 3, recordedAt: new Date().toISOString(), snapshot: { routeId: 'RTE-071', startLocationId: 'شعبه میرداماد', startedAt: new Date(Date.now()-15*60000).toISOString(), missions: ['MIS-10482','MIS-10479','MIS-10455','MIS-10491'].map((missionId,index)=>({missionId,committedDeliveryAt:new Date(Date.now()+(index+2)*15*60000).toISOString()})), orderedStops: [
  {missionId:'MIS-10482',kind:'Pickup',locationId:'شعبه میرداماد',serviceSeconds:180,pickupReadyAt:null},{missionId:'MIS-10482',kind:'Delivery',locationId:'ونک، ملاصدرا',serviceSeconds:120,pickupReadyAt:null},{missionId:'MIS-10479',kind:'Pickup',locationId:'شعبه جردن',serviceSeconds:180,pickupReadyAt:null},{missionId:'MIS-10479',kind:'Delivery',locationId:'الهیه',serviceSeconds:120,pickupReadyAt:null},{missionId:'MIS-10455',kind:'Pickup',locationId:'پاسداران',serviceSeconds:180,pickupReadyAt:null},{missionId:'MIS-10491',kind:'Delivery',locationId:'ولیعصر',serviceSeconds:120,pickupReadyAt:null},
] } }

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

export default function RoutePlanWorkbench({ token, reloadKey, demoMode = false, onConnected }: Props) {
  const [routeIdDraft, setRouteIdDraft] = useState('')
  const [routeId, setRouteId] = useState('')
  const [plan, setPlan] = useState<RoutePlan | null>(demoMode ? demoRoutePlan : null)
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
      <article><span>مسیرهای فعال</span><strong>{demoMode ? '۲۸' : '—'}</strong></article>
      <article><span>مأموریت‌های Batched</span><strong>{demoMode ? '۷۴' : '—'}</strong></article>
      <article><span>مسیرهای در معرض خطر</span><strong>{demoMode ? '۴' : '—'}</strong></article>
      <article><span>میانگین مأموریت در مسیر</span><strong>{demoMode ? '۲٫۶' : '—'}</strong></article>
      <article><span>ظرفیت آزاد شبکه</span><strong>{demoMode ? '۳۱٪' : '—'}</strong></article>
    </section>
    <div className="route-control-layout">
      <aside className="route-list-panel queue-panel">
        <div className="route-list-heading"><span className="route-count-pill">{demoMode ? '۲۸ فعال' : '— فعال'}</span><h3>مسیرهای فعال</h3></div>
        <form className="route-list-search" onSubmit={submit}>
          <label htmlFor="route-plan-id" className="sr-only">جست‌وجوی مسیر یا پیک</label>
          <input id="route-plan-id" value={routeIdDraft} onChange={(event) => setRouteIdDraft(event.target.value)} dir="auto" maxLength={128} placeholder="جست‌وجوی مسیر یا پیک" autoComplete="off" />
          <button className="primary-button" disabled={!token || loading || !routeIdDraft.trim()}>{loading ? 'در حال دریافت…' : 'جست‌وجو'}</button>
        </form>
        {error && <div className="onboarding-error route-inline-error" role="alert">{error}</div>}
        {demoMode ? <div className="route-demo-list">{[['RTE-071','CR-204 • ۴ مأموریت • مرکزی','عادی'],['RTE-068','CR-118 • ۳ مأموریت • شمال','در معرض خطر'],['RTE-066','CR-302 • ۲ مأموریت • جنوب','عادی'],['RTE-063','CR-225 • ۵ مأموریت • شرق','عادی'],['RTE-061','بدون پیک • ۲ مأموریت • غرب','بحرانی'],['RTE-058','CR-174 • ۳ مأموریت • مرکزی','عادی']].map(([id,detail,state])=><button type="button" key={id} className={`route-demo-row${id==='RTE-071'?' selected':''}`} onClick={()=>{setRouteIdDraft(id);setPlan(demoRoutePlan)}}><span>{state}</span><strong dir="ltr">{id}</strong><small>{detail}</small></button>)}</div> : !token ? <p className="route-list-empty">برای خواندن مسیرها، ابتدا به API عملیات متصل شوید.</p> : <p className="route-list-empty">فهرست مسیرهای فعال هنوز از API در دسترس نیست. می‌توانید شناسهٔ یک مسیر ثبت‌شده را جست‌وجو کنید.</p>}
        {plan && <button className="route-selected-card" aria-current="true" onClick={() => void load(plan.snapshot.routeId)} disabled={loading}><span className="route-status-pill">نسخهٔ {fa.format(plan.version)}</span><strong dir="ltr">{plan.snapshot.routeId}</strong><small>{fa.format(plan.snapshot.missions.length)} مأموریت · نقطهٔ شروع {plan.snapshot.startLocationId}</small></button>}
      </aside>
      <section className="route-detail-panel queue-panel">
        {plan ? <>
          <section className="route-summary-card"><header className="route-detail-heading"><div><h3 dir="ltr">{plan.snapshot.routeId}</h3><p>{demoMode ? 'پیک CR-204 • موتورسیکلت' : 'پیک: اطلاعات در API نیست'} · {fa.format(plan.snapshot.missions.length)} مأموریت · {fa.format(stops.length)} توقف</p></div><span className="route-status-pill">{demoMode ? 'فعال' : 'وضعیت مسیر: نامشخص'}</span></header><div className="route-metrics">
            <div><span>پایان تخمینی</span><strong>{demoMode ? '۱۴:۵۸' : '—'}</strong></div><div><span>انحراف فعلی</span><strong>{demoMode ? '+۴ دقیقه' : '—'}</strong></div><div><span>ظرفیت استفاده‌شده</span><strong>{demoMode ? '۶۸٪' : '—'}</strong></div><div><span>ریسک استاندارد</span><strong>{demoMode ? '۱ مأموریت' : '—'}</strong></div>
          </div></section>
          <div className="route-detail-overview">
            <section className={`route-map-placeholder${demoMode?' has-demo-route':''}`} aria-label="نقشهٔ مسیر">{demoMode ? <><svg viewBox="0 0 600 280" role="img" aria-label="نقشهٔ عملیاتی نمونهٔ مسیر RTE-071"><path d="M54 210 C124 198 100 148 202 156 S310 197 354 123 S453 74 540 88"/><circle cx="54" cy="210" r="13"/><circle cx="202" cy="156" r="13"/><circle cx="354" cy="123" r="13"/><circle cx="540" cy="88" r="13"/></svg><strong>نقشه عملیاتی نمونه</strong><small>دادهٔ مکانی نمایشی است</small></> : <><span className="route-map-icon">⌖</span><strong>نقشهٔ مسیر</strong><p>مختصات توقف‌ها و موقعیت لحظه‌ای پیک در API موجود نیست.</p><small>شروع برنامه: {formatDate(plan.snapshot.startedAt)}</small></>}</section>
            <section className="route-optimization-card"><h3>فرصت بهینه‌سازی</h3><span className="route-status-pill muted">در دسترس نیست</span><p>پیشنهاد افزودن مأموریت پس از ارائهٔ دادهٔ مکان و API ارزیابی استاندارد فعال می‌شود.</p><dl><div><dt>مأموریت‌های مسیر</dt><dd>{fa.format(plan.snapshot.missions.length)}</dd></div><div><dt>توقف‌های ثبت‌شده</dt><dd>{fa.format(stops.length)}</dd></div><div><dt>اثر بر زمان مسیر</dt><dd>—</dd></div></dl><button disabled title="API ارزیابی و افزودن مأموریت به مسیر موجود نیست">افزودن به مسیر</button></section>
          </div>
          <section className="route-stop-section"><header><div><h3>ترتیب توقف‌ها</h3><p>نسخهٔ ثبت‌شدهٔ برنامهٔ مسیر</p></div><span className="route-status-pill muted">{fa.format(stops.length)} توقف</span></header><div className="table-wrap"><table><thead><tr><th>ترتیب</th><th>نوع</th><th>مأموریت</th><th>مکان</th><th>ETA</th><th>استاندارد خدمت</th><th>وضعیت</th></tr></thead><tbody>
            {stops.map((stop, index) => <tr key={`${index}-${stop.missionId}-${stop.locationId}`}>
              <td>{fa.format(index + 1)}</td><td>{stop.kind === 0 || stop.kind === 'Pickup' ? 'دریافت' : 'تحویل'}</td><td dir="ltr">{stop.missionId}</td><td dir="ltr">{stop.locationId}</td><td>{demoMode ? `${14+index}: ${index%2?'۴۰':'۲۷'}` : '—'}</td><td>{demoMode ? index===4?'در معرض خطر':'عادی' : '—'}</td><td>{demoMode ? 'در برنامه' : 'از API دریافت نمی‌شود'}</td>
            </tr>)}
            {stops.length === 0 && <tr><td colSpan={7} className="state-cell">برای این نسخه توقفی ثبت نشده است.</td></tr>}
          </tbody></table></div></section>
          <details className="route-audit-disclosure"><summary>سوابق ممیزی دسترسی مسیر</summary>{auditError && <div className="onboarding-error" role="alert">{auditError}</div>}<div className="table-wrap"><table><thead><tr><th>زمان</th><th>عامل</th><th>عملیات</th><th>نسخه</th><th>نتیجه</th><th>رکوردها</th></tr></thead><tbody>
            {audit?.items.map((item) => <tr key={item.audit_id}><td>{formatDate(item.occurred_at)}</td><td dir="ltr">{item.actor_id}</td><td>{actionLabels[item.action] || item.action}</td><td>{item.version ? fa.format(item.version) : '—'}</td><td dir="ltr">{item.outcome}</td><td>{fa.format(item.result_count)}</td></tr>)}
            {(!audit || audit.items.length === 0) && <tr><td colSpan={6} className="state-cell">در ۳۰ روز گذشته سابقه‌ای برای این مسیر پیدا نشد.</td></tr>}
          </tbody></table></div>{auditCursor && <div className="route-plan-audit-footer"><span>۵۰ رویداد اخیر نمایش داده شده</span><button className="secondary-button" onClick={() => void load(plan.snapshot.routeId, auditCursor)} disabled={loading}>رویدادهای قدیمی‌تر</button></div>}</details>
        </> : <>
          <section className="route-summary-card route-summary-empty"><header className="route-detail-heading"><div><h3>مسیر انتخاب‌شده</h3><p>برای دیدن جزئیات، یک مسیر ثبت‌شده را جست‌وجو کنید.</p></div><span className="route-status-pill muted">نامشخص</span></header><div className="route-metrics">
            <div><span>پایان تخمینی</span><strong>—</strong></div><div><span>انحراف فعلی</span><strong>—</strong></div><div><span>ظرفیت استفاده‌شده</span><strong>—</strong></div><div><span>ریسک استاندارد</span><strong>—</strong></div>
          </div></section>
          <div className="route-detail-overview">
            <section className="route-map-placeholder" aria-label="نقشهٔ مسیر"><span className="route-map-icon">⌖</span><strong>نقشهٔ مسیر</strong><p>پس از دریافت مختصات مسیر از API، نقشه در این بخش نمایش داده می‌شود.</p></section>
            <section className="route-optimization-card"><h3>فرصت بهینه‌سازی</h3><span className="route-status-pill muted">در دسترس نیست</span><p>برای پیشنهاد یا افزودن مأموریت، مسیر و دادهٔ ارزیابی استاندارد لازم است.</p><dl><div><dt>مأموریت‌های مسیر</dt><dd>—</dd></div><div><dt>توقف‌های ثبت‌شده</dt><dd>—</dd></div><div><dt>اثر بر زمان مسیر</dt><dd>—</dd></div></dl><button disabled title="ابتدا یک مسیر را بارگیری کنید">افزودن به مسیر</button></section>
          </div>
          <section className="route-stop-section"><header><div><h3>ترتیب توقف‌ها</h3><p>پس از بارگیری برنامهٔ مسیر</p></div><span className="route-status-pill muted">— توقف</span></header><div className="table-wrap"><table><thead><tr><th>ترتیب</th><th>نوع</th><th>مأموریت</th><th>مکان</th><th>ETA</th><th>استاندارد خدمت</th><th>وضعیت</th></tr></thead><tbody><tr><td colSpan={7} className="state-cell">برای نمایش توقف‌ها، یک مسیر را جست‌وجو کنید.</td></tr></tbody></table></div></section>
        </>}
      </section>
    </div>
  </main>
}
