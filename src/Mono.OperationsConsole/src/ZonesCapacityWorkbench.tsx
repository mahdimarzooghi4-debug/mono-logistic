import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import './zones-capacity-workbench.css'

type Zone = { zoneId: string; kind: string; geometryVersion: string; sourceId: string; sourceRevision: string; isActive: boolean; integrityVerified: boolean }
type Catalog = { items: Zone[]; hasMore: boolean; nextCursor: string | null }
type Bucket = { from: string; to: string; planned_courier_count: number }
type Projection = { zone_id: string; bucket_minutes: number; buckets: Bucket[] }
type Window = { window_id: string; courier_id: string; zone_id: string; starts_at: string; ends_at: string; status: string; created_by: string; created_at: string }
type WindowPage = { items: Window[]; next_cursor: string | null }
type Feature = { type: string; geometry: { type: string; coordinates: unknown }; properties: { zoneId: string; geometryVersion: string } }
type Props = { token: string; onConnected: (connected: boolean) => void }

const fa = new Intl.NumberFormat('fa-IR')
const faDate = (value: string) => new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
const zoneLabel = (zone: Zone) => zone.zoneId

export default function ZonesCapacityWorkbench({ token, onConnected }: Props) {
  const [catalog, setCatalog] = useState<Zone[]>([])
  const [catalogError, setCatalogError] = useState('')
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [bucketMinutes, setBucketMinutes] = useState('30')
  const [projection, setProjection] = useState<Projection | null>(null)
  const [windows, setWindows] = useState<Window[]>([])
  const [feature, setFeature] = useState<Feature | null>(null)
  const [loading, setLoading] = useState(false)
  const [capacityError, setCapacityError] = useState('')
  const [writeError, setWriteError] = useState('')
  const [writeMessage, setWriteMessage] = useState('')
  const [courierId, setCourierId] = useState('')
  const [cancelId, setCancelId] = useState('')
  const [cancelReason, setCancelReason] = useState('')

  const selected = catalog.find((item) => item.zoneId === selectedId) || null
  const visibleZones = useMemo(() => catalog.filter((item) => item.zoneId.toLocaleLowerCase().includes(query.toLocaleLowerCase())), [catalog, query])
  const plannedPeak = Math.max(0, ...(projection?.buckets.map((item) => item.planned_courier_count) || []))
  const totalWindows = windows.filter((item) => item.status === 'SCHEDULED').length

  const loadCatalog = useCallback(async () => {
    if (!token) { setCatalog([]); return }
    setCatalogError('')
    try {
      const response = await fetch('/operations/v1/zones/geometry-catalog?pageSize=100', { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await response.text())
      const page = await response.json() as Catalog
      if (!Array.isArray(page.items)) throw new Error('ساختار فهرست هندسهٔ زون نامعتبر است.')
      const zones = page.items.map((item) => ({ zoneId: item.zoneId, kind: item.kind, geometryVersion: item.geometryVersion, sourceId: item.sourceId, sourceRevision: item.sourceRevision, isActive: item.isActive, integrityVerified: item.integrityVerified }))
      setCatalog(zones)
      setSelectedId((current) => zones.some((zone) => zone.zoneId === current) ? current : zones.find((zone) => zone.isActive)?.zoneId || zones[0]?.zoneId || '')
      onConnected(true)
    } catch (cause) { setCatalogError(cause instanceof Error ? cause.message : 'فهرست زون‌ها دریافت نشد.'); onConnected(false) }
  }, [token, onConnected])

  useEffect(() => { void loadCatalog() }, [loadCatalog])
  useEffect(() => {
    if (!token || !selected || !selected.isActive) { setFeature(null); return }
    let cancelled = false
    fetch(`/operations/v1/zones/active-geometries/${encodeURIComponent(selected.kind)}?pageSize=25`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      .then(async (response) => { if (!response.ok) throw new Error(); return response.json() })
      .then((page: { geoJson?: { features?: Feature[] } }) => { if (!cancelled) setFeature(page.geoJson?.features?.find((item) => item.properties.zoneId === selected.zoneId) || null) })
      .catch(() => { if (!cancelled) setFeature(null) })
    return () => { cancelled = true }
  }, [token, selected])

  async function calculate(event?: FormEvent) {
    event?.preventDefault()
    if (!token || !selectedId || !from || !to) { setCapacityError('زون فعال و بازهٔ زمانی را مشخص کنید.'); return }
    const start = new Date(from); const end = new Date(to); const step = Number(bucketMinutes)
    if (Number.isNaN(start.valueOf()) || Number.isNaN(end.valueOf()) || end <= start || end.getTime() - start.getTime() > 24 * 60 * 60 * 1000 || ![15, 30, 60].includes(step) || start.getUTCMinutes() % step || end.getUTCMinutes() % step || (end.getTime() - start.getTime()) / 60000 % step) {
      setCapacityError('بازه حداکثر ۲۴ ساعت باشد و شروع، پایان و طول آن با گام UTC انتخاب‌شده هم‌تراز شود.')
      return
    }
    setLoading(true); setCapacityError(''); setWriteError(''); setWriteMessage('')
    const common = `zone_id=${encodeURIComponent(selectedId)}&from=${encodeURIComponent(start.toISOString())}&to=${encodeURIComponent(end.toISOString())}`
    try {
      const [projectionResponse, windowsResponse] = await Promise.all([
        fetch(`/operations/v1/courier-capacity-projection?${common}&bucket_minutes=${step}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
        fetch(`/operations/v1/courier-availability-windows?${common}&page_size=50`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      ])
      if (!projectionResponse.ok) throw new Error(await projectionResponse.text())
      if (!windowsResponse.ok) throw new Error(await windowsResponse.text())
      const [nextProjection, nextWindows] = await Promise.all([projectionResponse.json() as Promise<Projection>, windowsResponse.json() as Promise<WindowPage>])
      if (!Array.isArray(nextProjection.buckets) || !Array.isArray(nextWindows.items)) throw new Error('ساختار پاسخ ظرفیت یا پنجره‌های کاری معتبر نیست.')
      setProjection(nextProjection); setWindows(nextWindows.items); onConnected(true)
    } catch (cause) { setProjection(null); setWindows([]); setCapacityError(cause instanceof Error ? cause.message : 'دریافت ظرفیت ناموفق بود.'); onConnected(false) }
    finally { setLoading(false) }
  }

  async function createWindow() {
    if (!token || !selected || !selected.isActive) { setWriteError('برای این عمل باید زون فعال و توکن مدیر عملیات موجود باشد.'); return }
    const courier = courierId.trim(); const start = new Date(from); const end = new Date(to)
    if (!/^crr_[0-9a-f]{32}$/.test(courier)) { setWriteError('شناسهٔ پیک باید با قالب crr_ و ۳۲ رقم هگزادسیمال وارد شود.'); return }
    if (!from || !to || Number.isNaN(start.valueOf()) || Number.isNaN(end.valueOf()) || start < new Date() || start > new Date(Date.now() + 30 * 86400000) || end <= start || end.getTime() - start.getTime() > 16 * 3600000) { setWriteError('پنجره باید در ۳۰ روز آینده باشد و حداکثر ۱۶ ساعت طول بکشد.'); return }
    setWriteError(''); setWriteMessage('')
    try {
      const response = await fetch(`/operations/v1/couriers/${encodeURIComponent(courier)}/availability-windows`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, Accept: 'application/json', 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ zone_id: selected.zoneId, starts_at: start.toISOString(), ends_at: end.toISOString() }), cache: 'no-store' })
      if (!response.ok) throw new Error(await response.text())
      const window = await response.json() as Window
      setWindows((current) => [...current.filter((item) => item.window_id !== window.window_id), window]); setWriteMessage('پنجرهٔ کاری ثبت شد؛ این برنامه‌ریزی به معنی حضور زنده یا تخصیص پیک نیست.'); setCourierId('')
    } catch (cause) { setWriteError(cause instanceof Error ? cause.message : 'ثبت پنجرهٔ کاری ناموفق بود.') }
  }

  async function cancelWindow() {
    if (!token || !cancelId || cancelReason.trim().length < 2 || cancelReason.trim().length > 128) { setWriteError('دلیل لغو باید بین ۲ تا ۱۲۸ نویسه باشد.'); return }
    try {
      const response = await fetch(`/operations/v1/courier-availability-windows/${encodeURIComponent(cancelId)}/cancel`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, Accept: 'application/json', 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ reason: cancelReason.trim() }), cache: 'no-store' })
      if (!response.ok) throw new Error(await response.text())
      const cancelled = await response.json() as Window
      setWindows((current) => current.map((item) => item.window_id === cancelled.window_id ? cancelled : item)); setCancelId(''); setCancelReason(''); setWriteError(''); setWriteMessage('پنجرهٔ کاری لغو شد و دلیل در سابقهٔ آن ثبت شد.')
    } catch (cause) { setWriteError(cause instanceof Error ? cause.message : 'لغو پنجره ناموفق بود.') }
  }

  const path = useMemo(() => {
    if (!feature) return ''
    const pairs: Array<[number, number]> = []
    const walk = (value: unknown) => { if (Array.isArray(value) && value.length >= 2 && typeof value[0] === 'number' && typeof value[1] === 'number') pairs.push([value[0], value[1]]); else if (Array.isArray(value)) value.forEach(walk) }
    walk(feature.geometry.coordinates)
    if (pairs.length < 3) return ''
    const xs = pairs.map((item) => item[0]); const ys = pairs.map((item) => item[1]); const minX = Math.min(...xs); const maxX = Math.max(...xs); const minY = Math.min(...ys); const maxY = Math.max(...ys)
    const w = maxX - minX || 1; const h = maxY - minY || 1
    return pairs.map(([x, y], index) => `${index ? 'L' : 'M'}${12 + ((x - minX) / w) * 276},${172 - ((y - minY) / h) * 152}`).join(' ') + ' Z'
  }, [feature])

  const maxScale = Math.max(1, ...((projection?.buckets || []).map((item) => item.planned_courier_count)))

  return <div className="zones-capacity-workbench">
    <header className="zones-page-heading"><div><h1>مناطق و ظرفیت</h1><p>کنترل Micro-zoneها، تقاضا، ظرفیت مؤثر شبکه و ریسک کمبود پیک</p></div><div className="zones-heading-actions"><button className="secondary-button" disabled title="تعریف زون از این پنل پشتیبانی نمی‌شود؛ ابتدا snapshot هندسه باید ثبت و تأیید شود.">تعریف محدوده</button><button className="primary-button" onClick={() => document.getElementById('zone-capacity-form')?.scrollIntoView({ behavior: 'smooth', block: 'center' })}>برنامه ظرفیت</button></div></header>
    <section className="zones-kpis" aria-label="خلاصهٔ مناطق و ظرفیت">
      <article><small>مناطق فعال</small><strong>{token ? fa.format(catalog.filter((zone) => zone.isActive).length) : '—'}</strong><span>هندسهٔ فعال در کاتالوگ API</span></article>
      <article><small>پیک آنلاین</small><strong>—</strong><span>آمار حضور شبکه در API این نما نیست</span></article>
      <article><small>ظرفیت مؤثر</small><strong>—</strong><span>نیازمند تجمیع ظرفیت همهٔ زون‌ها</span></article>
      <article><small>مناطق کم‌ظرفیت</small><strong>—</strong><span>شاخص ریسک شبکه موجود نیست</span></article>
      <article><small>مأموریت بدون پوشش</small><strong>—</strong><span>در این API برگردانده نمی‌شود</span></article>
    </section>
    <div className="zones-workspace">
      <aside className="zones-directory panel-card">
        <header><h2>مناطق عملیاتی</h2><span>{token ? fa.format(visibleZones.length) : '—'} زون</span></header>
        <input aria-label="جست‌وجوی شناسهٔ زون" placeholder="جست‌وجوی شناسهٔ زون" value={query} onChange={(event) => setQuery(event.target.value)} />
        {catalogError && <p className="zones-inline-error" role="alert">دریافت کاتالوگ زون ناموفق بود. برای فهرست واقعی زون‌ها توکن مدیر عملیات را وارد کنید.</p>}
        <div className="zones-list">{visibleZones.map((zone) => <button key={`${zone.zoneId}-${zone.geometryVersion}`} className={`zone-list-item ${zone.zoneId === selectedId ? 'selected' : ''}`} onClick={() => { setSelectedId(zone.zoneId); setProjection(null); setWindows([]) }}>
          <span className={`zone-state ${zone.isActive ? 'active' : 'inactive'}`}>{zone.isActive ? 'فعال' : 'پیش‌نویس'}</span><strong dir="ltr">{zoneLabel(zone)}</strong><small>{zone.kind} · نسخهٔ {zone.geometryVersion}</small>
        </button>)}{!visibleZones.length && <p className="zones-empty-list">{token ? 'زون هندسی در کاتالوگ ثبت نشده است.' : 'برای نمایش فهرست واقعی، به API عملیات متصل شوید.'}</p>}</div>
      </aside>
      <main className="zones-main">
        <section className="zone-selected panel-card">
          <header><div><h2>{selected ? `زون ${selected.zoneId}` : 'یک زون را انتخاب کنید'}</h2><p>{selected ? `${selected.kind} · نسخهٔ هندسه ${selected.geometryVersion} · منبع ${selected.sourceId}` : 'اطلاعات محدوده و ظرفیت پس از دریافت کاتالوگ API نمایش داده می‌شود.'}</p></div><span className={`zone-state ${selected?.isActive ? 'active' : 'inactive'}`}>{selected ? selected.isActive ? 'فعال' : 'غیرفعال' : '—'}</span></header>
          <div className="zone-metrics"><div><small>وضعیت هندسه</small><strong>{selected?.integrityVerified ? 'یکپارچگی تأییدشده' : selected ? 'تأیید نشده' : '—'}</strong></div><div><small>ریسک ظرفیت</small><strong className={projection && plannedPeak === 0 ? 'risk-high' : 'risk-unknown'}>{projection ? plannedPeak === 0 ? 'نیازمند اقدام' : 'بر اساس برنامه' : 'نامشخص'}</strong></div><div><small>توقف‌های برنامه‌ریزی‌شده</small><strong>{projection ? fa.format(totalWindows) : '—'}</strong></div><div><small>مبنای محاسبه</small><strong>{projection ? `${fa.format(projection.bucket_minutes)} دقیقه` : 'بازه انتخاب نشده'}</strong></div></div>
        </section>
        <div className="zones-visual-row">
          <section className="zone-map-card panel-card"><header><h3>نقشهٔ محدوده</h3><small>{feature ? 'هندسهٔ فعال از API' : 'هندسهٔ محدوده در دسترس نیست'}</small></header><div className="zone-map-canvas" dir="ltr">
            <div className="map-grid-lines" />{path ? <svg viewBox="0 0 300 190" role="img" aria-label={`هندسهٔ زون ${selected?.zoneId}`}><path className="zone-boundary" d={path} /><circle className="zone-center" cx="150" cy="95" r="4" /></svg> : <div className="zone-map-empty"><span>⌖</span><strong>{token ? 'محدودهٔ واقعی برای نمایش موجود نیست' : 'برای دریافت نقشه به API متصل شوید'}</strong><small>نقشهٔ شهری یا مرز زون از دادهٔ ساختگی ساخته نمی‌شود.</small></div>}
          </div><footer><span>منبع: {selected?.sourceId || '—'}</span><span>بازبینی هندسه فقط پس از ثبت دادهٔ معتبر ممکن است.</span></footer></section>
          <section className="zone-capacity-card panel-card"><header><h3>ظرفیت برنامه‌ریزی‌شده</h3><span>{projection ? `${fa.format(projection.buckets.length)} بازه` : 'بدون محاسبه'}</span></header>{projection ? <><div className="capacity-summary"><strong>{fa.format(plannedPeak)}</strong><small>بیشینهٔ پیک برنامه‌ریزی‌شده در هر بازه</small></div><div className="capacity-chart" aria-label="نمودار ظرفیت">{projection.buckets.map((item) => <div key={item.from} title={`${faDate(item.from)} · ${fa.format(item.planned_courier_count)} پیک`}><i style={{ height: `${Math.max(3, item.planned_courier_count / maxScale * 76)}%` }} /><small>{new Intl.DateTimeFormat('fa-IR', { hour: '2-digit', minute: '2-digit' }).format(new Date(item.from))}</small></div>)}</div></> : <div className="capacity-no-data"><span>◷</span><strong>ظرفیت زون مشخص نشده</strong><p>زون و بازهٔ زمانی را انتخاب کنید تا API ظرفیت را از پنجره‌های کاری ثبت‌شده محاسبه کند.</p></div>}
            <form id="zone-capacity-form" className="zone-capacity-form" onSubmit={calculate}><label>از زمان<input type="datetime-local" value={from} onChange={(event) => setFrom(event.target.value)} /></label><label>تا زمان<input type="datetime-local" value={to} onChange={(event) => setTo(event.target.value)} /></label><label>گام زمانی<select value={bucketMinutes} onChange={(event) => setBucketMinutes(event.target.value)}><option value="15">۱۵ دقیقه</option><option value="30">۳۰ دقیقه</option><option value="60">۶۰ دقیقه</option></select></label><button className="primary-button" type="submit" disabled={!token || loading || !selected?.isActive}>{loading ? 'در حال محاسبه…' : 'محاسبهٔ ظرفیت'}</button></form>
            {capacityError && <p className="zones-inline-error" role="alert">{capacityError}</p>}</section>
        </div>
        <div className="zones-bottom-row">
          <section className="zone-table-card panel-card"><header><h3>تقاضا و ظرفیت منطقه</h3><small>{projection ? `زون ${projection.zone_id}` : 'پس از محاسبهٔ ظرفیت'}</small></header><div className="zone-table-wrap"><table><thead><tr><th>وضعیت</th><th>هدف</th><th>اکنون</th><th>شاخص</th></tr></thead><tbody>{projection?.buckets.map((item) => <tr key={item.from}><td>{item.planned_courier_count > 0 ? 'برنامه‌ریزی‌شده' : 'بدون پیک برنامه‌ریزی‌شده'}</td><td>{fa.format(item.planned_courier_count)}</td><td>—</td><td>{faDate(item.from)}</td></tr>)}{!projection && <tr><td colSpan={4} className="zone-table-empty">دادهٔ تقاضا در API موجود نیست؛ فقط پنجره‌های برنامه‌ریزی پیک قابل محاسبه‌اند.</td></tr>}</tbody></table></div><p>عدد برنامه‌ریزی‌شده بیانگر حضور زنده یا مجوز تخصیص نیست.</p></section>
          <section className="zone-actions-card panel-card"><header><h3>اقدام پیشنهادی</h3><span className="zone-state inactive">{projection && plannedPeak === 0 ? 'نیازمند اقدام' : 'بر پایهٔ دادهٔ API'}</span></header><p>برای برنامه‌ریزی شیفت پیک، شناسهٔ معتبر پیک و بازهٔ زمانی را ثبت کنید. ثبت برنامه ظرفیت لحظه‌ای را تغییر نمی‌دهد.</p><label>شناسهٔ پیک<input value={courierId} onChange={(event) => setCourierId(event.target.value)} placeholder="crr_…" dir="ltr" autoComplete="off" /></label><button className="primary-button" onClick={() => void createWindow()} disabled={!token || !selected?.isActive}>ثبت پنجرهٔ کاری</button><button className="secondary-button" disabled title="افزایش ظرفیت خودکار از API پشتیبانی نمی‌شود.">درخواست پیک بیشتر</button>{(writeError || writeMessage) && <p className={writeError ? 'zones-inline-error' : 'zones-success'} role={writeError ? 'alert' : 'status'}>{writeError || writeMessage}</p>}</section>
        </div>
        <section className="zone-windows panel-card"><header><h3>پنجره‌های کاری این بازه</h3><small>{projection ? fa.format(windows.length) : 'بازه را محاسبه کنید'}</small></header><div className="zone-table-wrap"><table><thead><tr><th>پیک</th><th>شروع</th><th>پایان</th><th>وضعیت</th><th>عملیات</th></tr></thead><tbody>{windows.map((item) => <tr key={item.window_id}><td dir="ltr">{item.courier_id}</td><td>{faDate(item.starts_at)}</td><td>{faDate(item.ends_at)}</td><td>{item.status === 'SCHEDULED' ? 'برنامه‌ریزی‌شده' : item.status}</td><td>{item.status === 'SCHEDULED' ? <button className="zone-cancel-link" onClick={() => { setCancelId(item.window_id); setWriteError('') }}>لغو پنجره</button> : '—'}</td></tr>)}{!windows.length && <tr><td colSpan={5} className="zone-table-empty">{projection ? 'پنجرهٔ کاری در این زون و بازه ثبت نشده است.' : 'برای مشاهدهٔ دادهٔ واقعی، ظرفیت یک زون و بازه را محاسبه کنید.'}</td></tr>}</tbody></table></div>
          {cancelId && <div className="zone-cancel-form"><strong>دلیل لغو پنجرهٔ {cancelId}</strong><input value={cancelReason} onChange={(event) => setCancelReason(event.target.value)} maxLength={128} placeholder="دلیل الزامی لغو" /><button className="primary-button" onClick={() => void cancelWindow()}>تأیید لغو</button><button className="secondary-button" onClick={() => setCancelId('')}>انصراف</button></div>}
        </section>
      </main>
    </div></div>
}
