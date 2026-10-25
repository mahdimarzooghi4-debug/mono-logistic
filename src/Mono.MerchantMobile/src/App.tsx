import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Branch = { branchId: string; name: string; address: string | null; status: string }
type BranchPage = { branches: Branch[] }
type Delivery = {
  delivery_request_id: string; external_reference: string; status: string; created_at: string
  mission_id: string | null; mission_status: string | null; committed_delivery_at: string | null
}
type DeliveryPage = { items: Delivery[]; next_cursor: string | null }
type Quote = { quote_id: string; version: number; expires_at: string; is_expired: boolean; distance_meters: number; fare_toman: number; buyer_share_toman: number; seller_share_toman: number }
type PromiseRecord = { promise_id: string; version: number; committed_delivery_at: string; outcome: string }
type Detail = Delivery & { quote: Quote | null; promise: PromiseRecord | null }
type CourierLocation = { latitude: number; longitude: number; accuracy_meters: number | null; observed_at: string }
type View = 'home' | 'deliveries' | 'create' | 'notifications' | 'more'
type ApiError = { error?: { code?: string; message?: string } }

const fa = new Intl.NumberFormat('fa-IR')
const money = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'medium', timeStyle: 'short' })
const API_BASE = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/$/, '')

function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date)
}

function statusText(value?: string | null) {
  const labels: Record<string, string> = {
    RECEIVED: 'ثبت‌شده', PENDING: 'در انتظار بررسی', QUOTED: 'پیشنهاد آمادهٔ بررسی',
    PROMISED: 'زمان تحویل تأییدشده', MATCHED: 'پیک تخصیص‌یافته', IN_PROGRESS: 'در حال ارسال',
    DELIVERED: 'تحویل‌شده', CANCELLED: 'لغوشده', FAILED: 'ناموفق',
  }
  return value ? labels[value.toUpperCase()] || value : 'در انتظار وضعیت'
}

async function api<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: { Accept: 'application/json', Authorization: `Bearer ${token}`,
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...init?.headers },
    cache: 'no-store',
  })
  if (!response.ok) {
    let reason = `خطای سرور (${response.status})`
    try { const body = await response.json() as ApiError; reason = body.error?.message || body.error?.code || reason } catch { /* keep generic response */ }
    throw new Error(reason)
  }
  return await response.json() as T
}

function newIdempotencyKey() {
  if (!globalThis.crypto?.randomUUID) throw new Error('مرورگر امن برای ثبت درخواست در دسترس نیست.')
  return globalThis.crypto.randomUUID()
}

export default function App() {
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [view, setView] = useState<View>('home')
  const [branches, setBranches] = useState<Branch[]>([])
  const [items, setItems] = useState<Delivery[]>([])
  const [detail, setDetail] = useState<Detail | null>(null)
  const [courierLocation, setCourierLocation] = useState<CourierLocation | null>(null)
  const [locationState, setLocationState] = useState<'loading' | 'available' | 'unavailable'>('unavailable')
  const [selectedBranch, setSelectedBranch] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<'active' | 'completed' | 'failed'>('active')
  const [confirming, setConfirming] = useState(false)
  const requestSequence = useRef(0)
  const createKey = useRef<{ fingerprint: string; key: string } | null>(null)
  const confirmKey = useRef<{ fingerprint: string; key: string } | null>(null)
  const [form, setForm] = useState({ reference: '', pickup: '', pickupPhone: '', dropoff: '', recipient: '', recipientPhone: '', details: '', bags: '1', ready: '' })

  const refresh = useCallback(async (activeToken: string) => {
    const sequence = ++requestSequence.current
    setLoading(true); setError('')
    try {
      const [branchPage, deliveryPage] = await Promise.all([
        api<BranchPage>('/v1/branches', activeToken),
        api<DeliveryPage>('/v1/delivery-requests?page_size=50', activeToken),
      ])
      if (sequence !== requestSequence.current) return
      setBranches(branchPage.branches.filter(branch => branch.status === 'ACTIVE'))
      setItems(deliveryPage.items)
      setSelectedBranch(current => current || branchPage.branches.find(branch => branch.status === 'ACTIVE')?.branchId || '')
    } catch (cause) {
      if (sequence === requestSequence.current) setError(cause instanceof Error ? cause.message : 'دریافت اطلاعات انجام نشد.')
    } finally { if (sequence === requestSequence.current) setLoading(false) }
  }, [])

  useEffect(() => { if (token) void refresh(token) }, [token, refresh])

  useEffect(() => {
    const missionId = detail?.mission_id
    if (!token || !missionId || ['DELIVERED', 'FAILED', 'CANCELLED'].includes((detail.mission_status || '').toUpperCase())) {
      setCourierLocation(null); setLocationState('unavailable')
      return
    }
    let active = true
    const loadLocation = async () => {
      try {
        const position = await api<CourierLocation>(`/v1/missions/${encodeURIComponent(missionId)}/courier-location`, token)
        if (active) { setCourierLocation(position); setLocationState('available') }
      } catch {
        if (active) { setCourierLocation(null); setLocationState('unavailable') }
      }
    }
    setLocationState('loading')
    void loadLocation()
    const timer = window.setInterval(() => void loadLocation(), 15_000)
    return () => { active = false; window.clearInterval(timer) }
  }, [token, detail?.mission_id, detail?.mission_status])

  const selectedBranchName = branches.find(branch => branch.branchId === selectedBranch)?.name || branches[0]?.name || 'شعبه‌ای انتخاب نشده'
  const activeItems = items.filter(item => !['DELIVERED', 'CANCELLED', 'FAILED'].includes((item.mission_status || item.status).toUpperCase()))
  const atRisk = activeItems.filter(item => /risk|late|delay/i.test(`${item.status} ${item.mission_status}`))
  const filtered = useMemo(() => items.filter(item => {
    const status = (item.mission_status || item.status).toUpperCase()
    const matchesFilter = filter === 'completed' ? status === 'DELIVERED' : filter === 'failed' ? ['FAILED', 'CANCELLED'].includes(status) : !['DELIVERED', 'FAILED', 'CANCELLED'].includes(status)
    const q = search.trim().toLocaleLowerCase()
    return matchesFilter && (!q || item.external_reference.toLocaleLowerCase().includes(q) || item.delivery_request_id.toLocaleLowerCase().includes(q))
  }), [items, filter, search])

  async function openDetail(item: Delivery) {
    if (!token) return
    setError(''); setNotice('')
    try { setDetail(await api<Detail>(`/v1/delivery-requests/${encodeURIComponent(item.delivery_request_id)}`, token)) }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'جزئیات ارسال دریافت نشد.') }
  }

  async function createDelivery(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(''); setNotice('')
    if (!selectedBranch) { setError('برای ثبت ارسال، یک شعبهٔ فعال انتخاب کنید.'); return }
    const payload = {
          external_reference: form.reference.trim(), branch_id: selectedBranch,
          pickup: { address_text: form.pickup.trim(), contact_phone: form.pickupPhone.trim() || null },
          dropoff: { address_text: form.dropoff.trim(), recipient_name: form.recipient.trim() || null, recipient_phone: form.recipientPhone.trim() || null, address_details: form.details.trim() || null },
          shipment: { shipment_model: 'BAG_BASED', bag_count: Number(form.bags), load_class: 'M', fragile: false, liquid: false, keep_upright: false },
          requested_service: 'URBAN_DELIVERY', requested_priority: 'STANDARD',
          estimated_ready_at: form.ready ? new Date(form.ready).toISOString() : null,
        }
    const fingerprint = JSON.stringify(payload)
    if (createKey.current?.fingerprint !== fingerprint) {
      try { createKey.current = { fingerprint, key: newIdempotencyKey() } }
      catch (cause) { setError(cause instanceof Error ? cause.message : 'ثبت امن درخواست ممکن نیست.'); return }
    }
    setLoading(true)
    try {
      const created = await api<{ delivery_request_id: string }>('/v1/delivery-requests', token, {
        method: 'POST', headers: { 'Idempotency-Key': createKey.current.key },
        body: fingerprint,
      })
      createKey.current = null
      await refresh(token)
      const createdDetail = await api<Detail>(`/v1/delivery-requests/${encodeURIComponent(created.delivery_request_id)}`, token)
      setDetail(createdDetail); setView('home'); setNotice('درخواست ارسال ثبت شد. پیشنهاد قیمت پس از آماده‌شدن در همین صفحه نمایش داده می‌شود.')
      setForm({ reference: '', pickup: '', pickupPhone: '', dropoff: '', recipient: '', recipientPhone: '', details: '', bags: '1', ready: '' })
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'ثبت ارسال انجام نشد.') }
    finally { setLoading(false) }
  }

  async function confirmOffer() {
    if (!detail?.quote || !detail.promise || detail.quote.is_expired) return
    const payload = { quote_id: detail.quote.quote_id, promise_version: detail.promise.version }
    const fingerprint = JSON.stringify(payload)
    if (confirmKey.current?.fingerprint !== fingerprint) {
      try { confirmKey.current = { fingerprint, key: newIdempotencyKey() } }
      catch (cause) { setError(cause instanceof Error ? cause.message : 'تأیید امن ممکن نیست.'); return }
    }
    setConfirming(true); setError('')
    try {
      await api(`/v1/delivery-requests/${encodeURIComponent(detail.delivery_request_id)}/confirm`, token, {
        method: 'POST', headers: { 'Idempotency-Key': confirmKey.current.key },
        body: fingerprint,
      })
      confirmKey.current = null
      const updated = await api<Detail>(`/v1/delivery-requests/${encodeURIComponent(detail.delivery_request_id)}`, token)
      setDetail(updated); await refresh(token); setNotice('پیشنهاد تأیید شد و درخواست برای تخصیص پیک ارسال شد.')
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'تأیید پیشنهاد انجام نشد.') }
    finally { setConfirming(false) }
  }

  function connect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const clean = tokenDraft.trim()
    if (!clean) return
    setToken(clean); setTokenDraft(''); setError(''); setNotice('')
  }
  function disconnect() { requestSequence.current++; createKey.current = null; confirmKey.current = null; setToken(''); setBranches([]); setItems([]); setDetail(null); setView('home'); setNotice('') }

  const nav: Array<{ id: View; title: string; icon: string }> = [
    { id: 'more', title: 'بیشتر', icon: '•••' }, { id: 'notifications', title: 'اعلان‌ها', icon: '♧' },
    { id: 'create', title: 'ثبت', icon: '+' }, { id: 'deliveries', title: 'ارسال‌ها', icon: '▤' }, { id: 'home', title: 'خانه', icon: '⌂' },
  ]

  return <main className="mobile-shell">
    <header className="app-header">
      <div className="brand-mark" aria-hidden="true">م</div>
      <div className="header-copy"><strong>{token ? 'سلام، کسب‌وکار شما' : 'اپ کسب‌وکار مونو'}</strong><span>{token ? selectedBranchName : 'برای شروع به حساب مشتری وصل شوید'}</span></div>
      {token && <button className="branch-picker" onClick={() => setView('more')} aria-label="انتخاب شعبه">⌄</button>}
    </header>

    <section className="page-content" aria-live="polite">
      {!token ? <section className="welcome-card">
        <span className="eyebrow">حساب کسب‌وکار</span><h1>ارسال‌هایتان را<br />از همین‌جا مدیریت کنید</h1>
        <p>برای نمایش شعب و ارسال‌های واقعی، توکن دسترسی مشتری را وارد کنید. توکن فقط تا پایان این نشست در حافظهٔ صفحه می‌ماند.</p>
        <form onSubmit={connect} className="stack-form"><label>توکن دسترسی<input aria-label="توکن دسترسی" type="password" autoComplete="off" value={tokenDraft} onChange={e => setTokenDraft(e.target.value)} required /></label><button className="primary-button">اتصال به حساب</button></form>
      </section> : <>
        {error && <div className="feedback error" role="alert">{error}<button onClick={() => setError('')} aria-label="بستن خطا">×</button></div>}
        {notice && <div className="feedback success" role="status">{notice}<button onClick={() => setNotice('')} aria-label="بستن پیام">×</button></div>}
        {loading && <div className="loading-line">در حال دریافت اطلاعات…</div>}

        {view === 'home' && <>
          <div className="section-head"><div><span className="eyebrow">نمای امروز</span><h1>امروز چه چیزی نیاز به توجه دارد؟</h1></div></div>
          {atRisk.length > 0 && <button className="risk-card" onClick={() => { setFilter('active'); setView('deliveries'); void openDetail(atRisk[0]) }}><span className="risk-icon">!</span><span><strong>{fa.format(atRisk.length)} ارسال نیازمند توجه</strong><small>برای دیدن وضعیت ارسال‌ها باز کنید</small></span><b>‹</b></button>}
          <div className="section-head compact"><h2>ارسال‌های فعال</h2><button className="text-button" onClick={() => setView('deliveries')}>همه</button></div>
          <div className="summary-card"><div><b>{fa.format(activeItems.length)}</b><span>در حال اجرا</span></div><i /><div><b>{fa.format(atRisk.length)}</b><span>نیازمند توجه</span></div></div>
          <button className="primary-button full" onClick={() => setView('create')}>＋ ثبت ارسال جدید</button>
          <div className="section-head compact"><h2>آخرین ارسال‌ها</h2><button className="text-button" onClick={() => setView('deliveries')}>مشاهده همه</button></div>
          <div className="delivery-list">{items.slice(0, 5).map(item => <DeliveryCard key={item.delivery_request_id} item={item} onClick={() => void openDetail(item)} />)}
            {!items.length && !loading && <EmptyState title="هنوز ارسالی ثبت نشده" body="با ثبت اولین ارسال، وضعیت آن را همین‌جا دنبال کنید." />}</div>
        </>}

        {view === 'deliveries' && <>
          <div className="section-head"><div><span className="eyebrow">همهٔ شعب</span><h1>ارسال‌ها</h1></div><button className="round-button" onClick={() => void refresh(token)} aria-label="به‌روزرسانی">↻</button></div>
          <div className="filter-tabs" role="tablist">{(['active', 'completed', 'failed'] as const).map((key, index) => <button role="tab" aria-selected={filter === key} className={filter === key ? 'selected' : ''} key={key} onClick={() => setFilter(key)}>{['فعال', 'تکمیل‌شده', 'ناموفق'][index]}</button>)}</div>
          <label className="search-box"><span>⌕</span><input aria-label="جست‌وجوی سفارش" placeholder="جست‌وجوی شماره سفارش" value={search} onChange={e => setSearch(e.target.value)} /></label>
          <div className="delivery-list">{filtered.map(item => <DeliveryCard key={item.delivery_request_id} item={item} onClick={() => void openDetail(item)} />)}{!filtered.length && !loading && <EmptyState title="ارسالی پیدا نشد" body={search ? 'عبارت جست‌وجو یا فیلتر وضعیت را تغییر دهید.' : 'برای این فیلتر اطلاعاتی از سرور دریافت نشد.'} />}</div>
        </>}

        {view === 'create' && <>
          <div className="section-head"><div><span className="eyebrow">مرحلهٔ ۱ از ۲</span><h1>ثبت ارسال جدید</h1></div><span className="step-pill">اطلاعات ارسال</span></div>
          {branches.length === 0 ? <EmptyState title="شعبهٔ فعالی در دسترس نیست" body="برای ثبت ارسال، حساب باید دست‌کم یک شعبهٔ فعال داشته باشد." /> : <form className="stack-form create-form" onSubmit={createDelivery}>
            <label>شعبهٔ مبدأ<select aria-label="شعبهٔ مبدأ" value={selectedBranch} onChange={e => setSelectedBranch(e.target.value)} required>{branches.map(branch => <option key={branch.branchId} value={branch.branchId}>{branch.name}</option>)}</select></label>
            <label>شماره سفارش<input required maxLength={128} value={form.reference} onChange={e => setForm({ ...form, reference: e.target.value })} /></label>
            <div className="form-section"><b>مبدأ</b><label>نشانی مبدأ<input required maxLength={500} value={form.pickup} onChange={e => setForm({ ...form, pickup: e.target.value })} /></label><label>تلفن تماس مبدأ<input inputMode="tel" value={form.pickupPhone} onChange={e => setForm({ ...form, pickupPhone: e.target.value })} /></label></div>
            <div className="form-section"><b>مقصد</b><label>نشانی گیرنده<input required maxLength={500} value={form.dropoff} onChange={e => setForm({ ...form, dropoff: e.target.value })} /></label><label>نام گیرنده<input value={form.recipient} onChange={e => setForm({ ...form, recipient: e.target.value })} /></label><label>تلفن گیرنده<input inputMode="tel" value={form.recipientPhone} onChange={e => setForm({ ...form, recipientPhone: e.target.value })} /></label><label>جزئیات نشانی<textarea rows={2} value={form.details} onChange={e => setForm({ ...form, details: e.target.value })} /></label></div>
            <div className="form-row"><label>تعداد بسته<input type="number" min="1" max="100" value={form.bags} onChange={e => setForm({ ...form, bags: e.target.value })} required /></label><label>آماده‌سازی تقریبی<input type="datetime-local" value={form.ready} onChange={e => setForm({ ...form, ready: e.target.value })} /></label></div>
            <button className="primary-button" disabled={loading}>{loading ? 'در حال ثبت…' : 'ثبت درخواست و دریافت پیشنهاد'}</button>
            <p className="form-hint">هزینه و زمان تحویل پس از آماده‌شدن پیشنهاد در صفحهٔ جزئیات نمایش داده می‌شود.</p>
          </form>}
        </>}

        {view === 'notifications' && <><div className="section-head"><div><span className="eyebrow">به‌روزرسانی‌ها</span><h1>اعلان‌ها</h1></div></div><EmptyState title="مرکز اعلان‌ها هنوز به API وصل نیست" body="در حال حاضر سامانه مسیر ثبت و مشاهدهٔ اعلان‌های کسب‌وکار را ارائه نمی‌کند؛ مورد ساختگی نمایش داده نمی‌شود." /></>}
        {view === 'more' && <><div className="section-head"><div><span className="eyebrow">حساب کسب‌وکار</span><h1>بیشتر</h1></div></div><div className="settings-card"><p><span>شعبهٔ انتخاب‌شده</span><strong>{selectedBranchName}</strong></p>{branches.map(branch => <button key={branch.branchId} className="branch-option" onClick={() => { setSelectedBranch(branch.branchId); setView('home') }}><span>{branch.name}</span><span>{selectedBranch === branch.branchId ? '✓' : '›'}</span></button>)}<button className="danger-button" onClick={disconnect}>قطع اتصال از حساب</button><small>اطلاعات ورود در ذخیره‌ساز دستگاه ثبت نمی‌شود.</small></div></>}
      </>}
    </section>

    {detail && <div className="detail-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) setDetail(null) }}><section className="detail-sheet" role="dialog" aria-modal="true" aria-labelledby="detail-heading">
      <div className="sheet-grabber" /><header><div><span className="eyebrow">جزئیات ارسال</span><h2 id="detail-heading">سفارش {detail.external_reference}</h2></div><button className="round-button" onClick={() => setDetail(null)} aria-label="بستن">×</button></header>
      <div className="status-callout"><span className="status-dot" /><span><strong>{statusText(detail.mission_status || detail.status)}</strong><small>{detail.mission_id ? `شناسه مأموریت: ${detail.mission_id}` : `شناسه ارسال: ${detail.delivery_request_id}`}</small></span></div>
      {detail.mission_id && <section className="courier-location" aria-live="polite">
        <div className="courier-location-heading"><span className="eyebrow">موقعیت پیک</span><span className={`location-indicator ${locationState}`} /></div>
        {courierLocation ? <>
          <strong>موقعیت به‌روز پیک</strong>
          <p>آخرین دریافت: {formatDate(courierLocation.observed_at)}{courierLocation.accuracy_meters != null ? ` · دقت حدود ${fa.format(Math.round(courierLocation.accuracy_meters))} متر` : ''}</p>
          <div className="location-coordinates" dir="ltr">{courierLocation.latitude.toFixed(5)}, {courierLocation.longitude.toFixed(5)}</div>
          <a href={`geo:${courierLocation.latitude},${courierLocation.longitude}?q=${courierLocation.latitude},${courierLocation.longitude}`} className="location-map-link">بازکردن موقعیت در نقشهٔ دستگاه</a>
        </> : <p>{locationState === 'loading' ? 'در حال دریافت موقعیت پیک…' : 'موقعیت تازه‌ای برای این مأموریت در دسترس نیست.'}</p>}
        <small className="location-privacy">فقط آخرین موقعیت پیکِ مأموریت فعال نمایش داده می‌شود.</small>
      </section>}
      <dl className="detail-grid"><div><dt>زمان ثبت</dt><dd>{formatDate(detail.created_at)}</dd></div><div><dt>تعهد تحویل</dt><dd>{formatDate(detail.promise?.committed_delivery_at || detail.committed_delivery_at)}</dd></div></dl>
      <div className="timeline"><span className="timeline-line" /><div className="timeline-step done"><i /><span><b>ثبت درخواست</b><small>{formatDate(detail.created_at)}</small></span></div><div className={`timeline-step ${detail.quote ? 'done' : ''}`}><i /><span><b>پیشنهاد مونو</b><small>{detail.quote ? 'پیشنهاد آمادهٔ بررسی' : 'در انتظار آماده‌شدن پیشنهاد'}</small></span></div><div className={`timeline-step ${detail.mission_id ? 'done' : ''}`}><i /><span><b>تخصیص پیک</b><small>{detail.mission_id ? statusText(detail.mission_status) : 'پس از تأیید پیشنهاد'}</small></span></div></div>
      {detail.quote && <div className="offer-card"><span className="eyebrow">پیشنهاد مونو · نسخهٔ {fa.format(detail.quote.version)}</span><strong>{money.format(detail.quote.fare_toman)} تومان</strong><div><span>مسافت</span><b>{fa.format(detail.quote.distance_meters)} متر</b></div><div><span>سهم شما</span><b>{money.format(detail.quote.seller_share_toman)} تومان</b></div><div><span>اعتبار</span><b>{detail.quote.is_expired ? 'منقضی شده' : formatDate(detail.quote.expires_at)}</b></div><div><span>تعهد تحویل</span><b>{formatDate(detail.promise?.committed_delivery_at)}</b></div></div>}
      {!detail.quote && <div className="pending-note">پیشنهاد قیمت و زمان تحویل هنوز از سرویس دریافت نشده است.</div>}
      {detail.quote && detail.promise && !detail.mission_id && <button className="primary-button full" disabled={confirming || detail.quote.is_expired} onClick={() => void confirmOffer()}>{confirming ? 'در حال تأیید…' : detail.quote.is_expired ? 'پیشنهاد منقضی شده' : 'تأیید پیشنهاد و درخواست پیک'}</button>}
      <a className="support-link" href={`tel:${import.meta.env.VITE_SUPPORT_PHONE || ''}`} onClick={event => { if (!import.meta.env.VITE_SUPPORT_PHONE) event.preventDefault() }}>تماس با پشتیبانی</a>
    </section></div>}

    {token && <nav className="bottom-nav" aria-label="ناوبری اصلی">{nav.map(item => <button key={item.id} className={`${view === item.id ? 'active' : ''} ${item.id === 'create' ? 'create-tab' : ''}`} onClick={() => { setView(item.id); setError(''); setNotice('') }} aria-current={view === item.id ? 'page' : undefined}><span>{item.icon}</span><small>{item.title}</small></button>)}</nav>}
  </main>
}

function DeliveryCard({ item, onClick }: { item: Delivery; onClick: () => void }) {
  const status = item.mission_status || item.status
  const risky = /risk|late|delay/i.test(`${item.status} ${item.mission_status}`)
  return <button className="delivery-card" onClick={onClick}><span className="delivery-symbol">↙</span><span className="delivery-main"><strong>سفارش {item.external_reference || item.delivery_request_id.slice(-6)}</strong><small>ثبت {formatDate(item.created_at)}</small></span><span className={`status-badge ${risky ? 'warning' : ''}`}>{risky ? 'نیازمند توجه' : statusText(status)}</span><span className="card-arrow">‹</span></button>
}

function EmptyState({ title, body }: { title: string; body: string }) {
  return <div className="empty-state"><span className="empty-icon">⌂</span><strong>{title}</strong><p>{body}</p></div>
}
