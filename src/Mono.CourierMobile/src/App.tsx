import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
const RouteMap = lazy(() => import('./RouteMap'))

type Coordinate = { latitude: number; longitude: number } | null
type Offer = { offer_id: string; mission_id: string; status: string; created_at: string; expires_at: string }
type OfferPage = Offer[] | { items?: Offer[]; offers?: Offer[] }
type RoutePoint = { location_id: string; coordinate: Coordinate }
type RouteContext = { offer_id: string; mission_id: string; expires_at: string; coordinates_status: string; pickup: RoutePoint; dropoff: RoutePoint }
type ActiveMission = Omit<RouteContext, 'expires_at'> & { status: string }
const nextEvents: Record<string, { type: string; label: string }> = {
  COURIER_ASSIGNED: { type: 'mission.arrived_pickup', label: 'رسیدم به مبدأ' }, ARRIVED_PICKUP: { type: 'mission.handed_to_courier', label: 'تحویل مرسوله را تأیید می‌کنم' },
  HANDED_TO_COURIER: { type: 'mission.picked_up', label: 'مرسوله را تحویل گرفتم' }, PICKED_UP: { type: 'mission.in_transit', label: 'حرکت به سمت مقصد' },
  IN_TRANSIT: { type: 'mission.arrived_dropoff', label: 'رسیدم به مقصد' }, ARRIVED_DROPOFF: { type: 'mission.delivered', label: 'تحویل به گیرنده انجام شد' },
}
const dropoffStatuses = new Set(['PICKED_UP', 'IN_TRANSIT', 'ARRIVED_DROPOFF'])
const missionLabels: Record<string, string> = { COURIER_ASSIGNED: 'پیک تخصیص‌یافته', ARRIVED_PICKUP: 'رسیده به مبدأ', HANDED_TO_COURIER: 'تحویل از فروشگاه تأییدشده', PICKED_UP: 'مرسوله دریافت‌شده', IN_TRANSIT: 'در مسیر مقصد', ARRIVED_DROPOFF: 'رسیده به مقصد' }
type ApiError = { error?: { code?: string; message?: string } }
class ApiRequestError extends Error {
  constructor(readonly status: number, message: string) { super(message); this.name = 'ApiRequestError' }
}
const API_BASE = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/$/, '')
const MAP_STYLE_URL = import.meta.env.VITE_MAP_STYLE_URL || ''
const OFFER_REFRESH_INTERVAL_MS = 15_000
const datetime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'medium', timeStyle: 'short' })
let fallbackKeySequence = 0

async function api<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: { Accept: 'application/json', Authorization: `Bearer ${token}`, ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...init?.headers },
    cache: 'no-store',
  })
  if (!response.ok) {
    let message = `خطای سرور (${response.status})`
    try { const body = await response.json() as ApiError; message = body.error?.message || body.error?.code || message } catch { /* keep generic message */ }
    throw new ApiRequestError(response.status, message)
  }
  return await response.json() as T
}
function validCoordinate(value: Coordinate): value is NonNullable<Coordinate> {
  return !!value && Number.isFinite(value.latitude) && value.latitude >= -90 && value.latitude <= 90 && Number.isFinite(value.longitude) && value.longitude >= -180 && value.longitude <= 180
}
function date(value?: string) { const parsed = value ? new Date(value) : null; return parsed && !Number.isNaN(parsed.valueOf()) ? datetime.format(parsed) : '—' }
function idempotencyKey() {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID()
  fallbackKeySequence++
  return `mono-${Date.now().toString(36)}-${fallbackKeySequence.toString(36)}-${Math.random().toString(36).slice(2)}`
}

export default function App() {
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [offers, setOffers] = useState<Offer[]>([])
  const [mission, setMission] = useState<ActiveMission | null>(null)
  const [route, setRoute] = useState<Pick<RouteContext, 'coordinates_status' | 'pickup' | 'dropoff'> | null>(null)
  const [busy, setBusy] = useState(false)
  const [responding, setResponding] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [authMessage, setAuthMessage] = useState('')
  const [mapError, setMapError] = useState('')
  const sequence = useRef(0)
  const busyRef = useRef(false)
  const actionKeys = useRef(new globalThis.Map<string, string>())

  function expireSession() {
    sequence.current++
    busyRef.current = false
    actionKeys.current.clear()
    setToken(''); setTokenDraft(''); setOffers([]); setMission(null); setRoute(null)
    setBusy(false); setResponding(false); setError(''); setNotice('')
    setAuthMessage('نشست شما معتبر نیست یا منقضی شده است. برای ادامه دوباره متصل شوید.')
  }

  const refresh = useCallback(async (activeToken: string) => {
    const current = ++sequence.current
    busyRef.current = true; setBusy(true); setError(''); setMapError('')
    try {
      const [session, response, activeResponse] = await Promise.all([
        api<{ courier_id: string; identity_domain: string }>('/courier/v1/session', activeToken),
        api<OfferPage>('/courier/v1/mission-offers', activeToken),
        api<{ mission: ActiveMission | null }>('/courier/v1/missions/active', activeToken),
      ])
      void session
      if (current === sequence.current && activeResponse.mission) { setMission(activeResponse.mission); setOffers([]); setRoute(activeResponse.mission); return }
      if (current === sequence.current) setMission(null)
      const list = Array.isArray(response) ? response : response.items ?? response.offers ?? []
      const active = list.find(item => item.status.toUpperCase() === 'OFFERED')
      if (!active) { if (current === sequence.current) { setOffers([]); setRoute(null) }; return }
      const context = await api<RouteContext>(`/courier/v1/mission-offers/${encodeURIComponent(active.offer_id)}/route-context`, activeToken)
      if (current === sequence.current) {
        setOffers([active])
        setRoute(previous => JSON.stringify(previous) === JSON.stringify(context) ? previous : context)
      }
    } catch (cause) {
      if (current === sequence.current && cause instanceof ApiRequestError && cause.status === 401) expireSession()
      else if (current === sequence.current) { setError(cause instanceof Error ? cause.message : 'دریافت پیشنهاد انجام نشد.'); setOffers([]); setMission(null); setRoute(null) }
    } finally { busyRef.current = false; if (current === sequence.current) setBusy(false) }
  }, [])

  useEffect(() => { if (token) void refresh(token) }, [token, refresh])
  useEffect(() => {
    if (!token || mission) return
    const refreshWhenVisible = () => {
      if (document.visibilityState === 'visible' && !busyRef.current) void refresh(token)
    }
    const timer = window.setInterval(refreshWhenVisible, OFFER_REFRESH_INTERVAL_MS)
    document.addEventListener('visibilitychange', refreshWhenVisible)
    return () => {
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', refreshWhenVisible)
    }
  }, [token, mission, refresh])

  function connect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const value = tokenDraft.trim()
    if (!value) return
    setOffers([]); setMission(null); setRoute(null); setNotice(''); setError(''); setAuthMessage(''); setToken(value); setTokenDraft('')
  }
  function disconnect() {
    sequence.current++; actionKeys.current.clear(); setToken(''); setOffers([]); setMission(null); setRoute(null); setError(''); setNotice(''); setAuthMessage('')
  }
  async function respond(action: 'accept' | 'decline') {
    const offer = offers[0]
    if (!offer || !token || responding) return
    const keyId = `${offer.offer_id}:${action}`
    let key = actionKeys.current.get(keyId)
    try { if (!key) { key = idempotencyKey(); actionKeys.current.set(keyId, key) } }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'ثبت امن پاسخ ممکن نیست.'); return }
    setResponding(true); setError(''); setNotice('')
    try {
      await api(`/courier/v1/mission-offers/${encodeURIComponent(offer.offer_id)}/${action}`, token, { method: 'POST', headers: { 'Idempotency-Key': key } })
      actionKeys.current.delete(keyId)
      setNotice(action === 'accept' ? 'پیشنهاد پذیرفته شد.' : 'پیشنهاد رد شد.')
      await refresh(token)
    } catch (cause) {
      if (cause instanceof ApiRequestError && cause.status === 401) expireSession()
      else setError(cause instanceof Error ? cause.message : 'ثبت پاسخ انجام نشد.')
    }
    finally { setResponding(false) }
  }


  async function advanceMission() {
    if (!mission || !token || responding) return
    const event = nextEvents[mission.status]
    if (!event) return
    const keyId = `${mission.mission_id}:${event.type}`
    let key = actionKeys.current.get(keyId)
    try { if (!key) { key = idempotencyKey(); actionKeys.current.set(keyId, key) } }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'ثبت امن وضعیت ممکن نیست.'); return }
    setResponding(true); setError(''); setNotice('')
    try {
      await api(`/courier/v1/missions/${encodeURIComponent(mission.mission_id)}/events`, token, {
        method: 'POST', headers: { 'Idempotency-Key': key },
        body: JSON.stringify({ event_type: event.type, reason: 'COURIER_MOBILE_CONFIRMED' }),
      })
      actionKeys.current.delete(keyId); setNotice('وضعیت مأموریت ثبت شد.'); await refresh(token)
    } catch (cause) {
      if (cause instanceof ApiRequestError && cause.status === 401) expireSession()
      else setError(cause instanceof Error ? cause.message : 'ثبت وضعیت مأموریت انجام نشد.')
    }
    finally { setResponding(false) }
  }

  const offer = offers[0]
  const readyRoute = route?.coordinates_status === 'COMPLETE' && validCoordinate(route.pickup.coordinate) && validCoordinate(route.dropoff.coordinate)
  const navigationPoint = mission
    ? dropoffStatuses.has(mission.status) ? mission.dropoff.coordinate : mission.pickup.coordinate
    : null
  const navigationLabel = mission
    ? dropoffStatuses.has(mission.status) ? 'مسیریابی تا مشتری' : 'مسیریابی تا فروشگاه'
    : ''

  return <main className="courier-shell" dir="rtl">
    <header className="topbar"><div className="brand">م</div><div className="brand-copy"><strong>مونو پیک</strong><span>{token ? 'پیشنهادهای مأموریت' : 'ورود پیک'}</span></div>{token && <><button className="icon-button" onClick={() => void refresh(token)} disabled={busy} aria-label="به‌روزرسانی پیشنهادها">↻</button><button className="logout" onClick={disconnect}>خروج</button></>}</header>
    <section className="content" aria-live="polite">
      {!token ? <section className="login-card"><span className="eyebrow">پنل پیک</span><h1>آمادهٔ دریافت مأموریت</h1><p>توکن دسترسی آزمایشی را وارد کنید. توکن فقط در حافظهٔ این صفحه نگهداری می‌شود و پس از بستن یا بارگذاری دوباره پاک خواهد شد.</p>{authMessage && <div className="notice error" role="alert">{authMessage}</div>}<form onSubmit={connect}><label htmlFor="access-token">توکن دسترسی</label><input id="access-token" type="password" autoComplete="off" value={tokenDraft} onChange={event => setTokenDraft(event.target.value)} required /><button className="primary" type="submit">اتصال</button></form></section> : <>
        {error && <div className="notice error" role="alert">{error}</div>}{notice && <div className="notice success" role="status">{notice}</div>}
        <div className="page-heading"><div><span className="eyebrow">مأموریت‌های شما</span><h1>{mission ? 'مأموریت در حال اجرا' : offer ? 'پیشنهاد جدید' : 'پیشنهادی ندارید'}</h1></div><span className={`live-dot ${busy ? 'loading' : ''}`}>{busy ? 'در حال دریافت' : mission ? 'مأموریت فعال' : 'بررسی هر ۱۵ ثانیه'}</span></div>
        {busy && !offer && !mission && <div className="empty-card">در حال بررسی پیشنهادهای تازه…</div>}
        {!busy && !offer && !mission && <div className="empty-card"><span className="empty-icon">⌁</span><strong>فعلاً مأموریتی برای شما نیست</strong><p>برای بررسی دوباره، دکمهٔ به‌روزرسانی را بزنید.</p><button className="secondary" onClick={() => void refresh(token)}>بررسی دوباره</button></div>}
        {mission && <article className="offer-card active-mission-card">
          <div className="offer-meta"><span className="offer-badge active-badge">{missionLabels[mission.status] || 'مأموریت فعال'}</span><span>شناسه: <b dir="ltr">{mission.mission_id}</b></span></div>
          <div className="map-wrap">{MAP_STYLE_URL && validCoordinate(mission.pickup.coordinate) && validCoordinate(mission.dropoff.coordinate) ? <Suspense fallback={<div className="map-canvas map-loading">در حال بارگذاری نقشه…</div>}><RouteMap route={mission} styleUrl={MAP_STYLE_URL} onError={setMapError} /></Suspense> : <div className="map-canvas" role="img" aria-label="پیش‌نمایش مسیر مأموریت"><span className="map-placeholder">{MAP_STYLE_URL ? 'نقشه پس از دریافت مختصات معتبر نمایش داده می‌شود' : 'نقشه پس از تنظیم سرویس اختصاصی نمایش داده می‌شود'}</span></div>}<div className="map-legend"><span><i className="pickup-pin" /> مبدأ</span><span><i className="dropoff-pin" /> مقصد</span></div></div>
          {!MAP_STYLE_URL && <div className="map-warning" role="status">سرویس نقشهٔ اختصاصی تنظیم نشده است.</div>}
          {mission.coordinates_status !== 'COMPLETE' && <div className="pending-note" role="status">مختصات مبدأ یا مقصد هنوز آماده نیست؛ تا تکمیل مختصات، مسیریابی فعال نمی‌شود.</div>}
          <div className="location-list"><div><i className="pickup-pin" /><span><b>مبدأ</b><small>محل دریافت مرسوله</small></span></div><div><i className="dropoff-pin" /><span><b>مقصد</b><small>محل تحویل مرسوله</small></span></div></div>
          {navigationPoint && validCoordinate(navigationPoint) && nextEvents[mission.status] && <a className="primary mission-navigation" href={`geo:${navigationPoint.latitude},${navigationPoint.longitude}?q=${navigationPoint.latitude},${navigationPoint.longitude}`} aria-label={navigationLabel}>{navigationLabel}</a>}
          <div className="mission-progress"><span className="eyebrow">مرحلهٔ جاری</span><strong>{missionLabels[mission.status] || mission.status}</strong></div>
          {nextEvents[mission.status] ? <button className="primary mission-action" disabled={responding} onClick={() => void advanceMission()}>{responding ? 'در حال ثبت…' : nextEvents[mission.status].label}</button> : <div className="pending-note">این مأموریت پایان یافته است.</div>}
        </article>}
        {offer && !mission && <article className="offer-card">
          <div className="offer-meta"><span className="offer-badge">پیشنهاد مأموریت</span><span>مهلت پاسخ: {date(offer.expires_at)}</span></div>
          <div className="map-wrap">{MAP_STYLE_URL && readyRoute ? <Suspense fallback={<div className="map-canvas map-loading">در حال بارگذاری نقشه…</div>}><RouteMap route={route!} styleUrl={MAP_STYLE_URL} onError={setMapError} /></Suspense> : <div className="map-canvas" role="img" aria-label="پیش‌نمایش نقشهٔ مأموریت"><span className="map-placeholder">{MAP_STYLE_URL ? 'پس از دریافت مختصات معتبر نقشه نمایش داده می‌شود' : 'نقشه پس از تنظیم سرویس اختصاصی نمایش داده می‌شود'}</span></div>}<div className="map-legend"><span><i className="pickup-pin" /> مبدأ</span><span><i className="dropoff-pin" /> مقصد</span></div></div>
          {mapError && <div className="map-warning" role="status">نقشه در دسترس نیست؛ نشانی واقعی فقط پس از آماده‌شدن مختصات نمایش داده می‌شود.</div>}
          {!MAP_STYLE_URL && <div className="map-warning" role="status">سرویس نقشهٔ اختصاصی تنظیم نشده است.</div>}
          {route?.coordinates_status !== 'COMPLETE' && <div className="pending-note" role="status">مختصات مبدأ یا مقصد هنوز آماده نیست؛ تا دریافت مختصات معتبر، هیچ نشانگری نمایش داده نمی‌شود.</div>}
          {readyRoute && <div className="location-list"><div><i className="pickup-pin" /><span><b>مبدأ</b><small>نقطهٔ دریافت مرسوله</small></span></div><div><i className="dropoff-pin" /><span><b>مقصد</b><small>نقطهٔ تحویل مرسوله</small></span></div></div>}
          <dl className="offer-details"><div><dt>شناسهٔ مأموریت</dt><dd>{offer.mission_id}</dd></div><div><dt>زمان دریافت پیشنهاد</dt><dd>{date(offer.created_at)}</dd></div></dl>
          <div className="actions"><button className="decline" disabled={responding} onClick={() => void respond('decline')}>{responding ? 'در حال ثبت…' : 'رد پیشنهاد'}</button><button className="primary" disabled={responding} onClick={() => void respond('accept')}>{responding ? 'در حال ثبت…' : 'قبول مأموریت'}</button></div>
        </article>}
      </>}
    </section>
    <footer className="map-attribution">© OpenStreetMap contributors</footer>
  </main>
}
