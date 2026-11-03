import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'

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
  const [demoPreview] = useState(() => new URLSearchParams(window.location.search).get('demo') === '1')
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [view, setView] = useState<View>('home')
  const [branches, setBranches] = useState<Branch[]>([])
  const [items, setItems] = useState<Delivery[]>([])
  const [detail, setDetail] = useState<Detail | null>(null)
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

  if (demoPreview) return <MerchantMobileDemo />

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

type DemoMobileScreen = 'home' | 'deliveries' | 'detail' | 'exception' | 'create' | 'review' | 'notifications' | 'more'

function MerchantMobileDemo() {
  const [screen,setScreen]=useState<DemoMobileScreen>('home')
  const [filter,setFilter]=useState<'active'|'completed'|'failed'>('active')
  const [search,setSearch]=useState('')
  const [order,setOrder]=useState('۸۴۲۱')
  const [confirmed,setConfirmed]=useState(false)
  const [toast,setToast]=useState('')
  const [notificationFilter,setNotificationFilter]=useState<'all'|'action'|'info'>('all')
  const [readNotificationIds,setReadNotificationIds]=useState<string[]>([])
  const [demoRecipient,setDemoRecipient]=useState('مریم اکبری')
  const [demoPhone,setDemoPhone]=useState('۰۹۱۲ ۱۲۳ ۴۵۶۷')
  const [demoDestination,setDemoDestination]=useState('سعادت‌آباد، بلوار دریا')
  const nav:[DemoMobileScreen,string,string][]=[['more','بیشتر','more'],['notifications','اعلان‌ها','bell'],['create','ثبت','plus'],['deliveries','ارسال‌ها','list'],['home','خانه','home']]
  const orders=[
    {id:'۸۴۳۰',route:'ونک ← یوسف‌آباد',detail:'تحویل تا ۱۹:۲۰',status:'فعال',warning:false},
    {id:'۸۴۲۱',route:'ونک ← جردن',detail:'تعهد ۱۸:۴۵ • ۱۲ دقیقه ریسک',status:'در معرض دیرکرد',warning:true},
    {id:'۸۴۱۸',route:'ونک ← میرداماد',detail:'تحویل شد ۱۹:۵۸',status:'تحویل‌شده',warning:false},
    {id:'۸۴۱۵',route:'ونک ← سعادت‌آباد',detail:'تحویل تا ۲۰:۳۰',status:'فعال',warning:false},
    {id:'۸۴۱۲',route:'ونک ← تهران‌پارس',detail:'ارسال لغو شد • ۱۸:۲۰',status:'ناموفق',warning:true},
    {id:'۸۴۱۰',route:'ونک ← شهرک غرب',detail:'تحویل تا ۲۰:۴۰',status:'فعال',warning:false},
  ]
  const demoNotifications=[
    {id:'risk-8421',day:'today',kind:'action' as const,title:'ریسک دیرکرد برای سفارش ۸۴۲۱',detail:'تعهد ۱۸:۴۵ • شعبه ونک',orderId:'۸۴۲۱'},
    {id:'assigned-8430',day:'today',kind:'info' as const,title:'سفارش ۸۴۳۰ به پیک تخصیص داده شد',detail:'تحویل تا ۱۹:۲۰',orderId:'۸۴۳۰'},
    {id:'handoff-8418',day:'today',kind:'info' as const,title:'سفارش ۸۴۱۸ از شعبه تحویل شد',detail:'در مسیر مقصد',orderId:'۸۴۱۸'},
    {id:'delivered-8398',day:'yesterday',kind:'info' as const,title:'سفارش ۸۳۹۸ تحویل شد',detail:'گیرنده: کیانا مرادی',orderId:'۸۳۹۸'},
    {id:'invoice-1',day:'yesterday',kind:'info' as const,title:'صورتحساب هفتگی صادر شد',detail:'دوره ۲۲ تا ۲۸ شهریور'},
  ]
  const headerTitle=screen==='home'?'سلام، فروشگاه سپهر':screen==='deliveries'?'ارسال‌ها':screen==='detail'?'سفارش '+order:screen==='create'?'ثبت ارسال':screen==='review'?'بررسی و تأیید':screen==='exception'?'استثنای ارسال':screen==='notifications'?'اعلان‌ها':'بیشتر'
  const headerSubtitle=screen==='exception'?'سفارش '+order:'شعبه ونک'
  function openDetail(id:string){setOrder(id);setScreen('detail');setToast('')}
  function openDemoNotification(item:typeof demoNotifications[number]) {
    setReadNotificationIds(ids=>ids.includes(item.id)?ids:[...ids,item.id])
    if(item.orderId) openDetail(item.orderId)
    else setToast('این صورتحساب فقط نمونهٔ نمایشی است و دادهٔ مالی واقعی نیست.')
  }
  function review(){setScreen('review');setToast('')}
  function confirm(){setConfirmed(true);setOrder('۸۴۳۱');setScreen('detail');setToast('درخواست نمایشی ثبت شد؛ در نسخهٔ واقعی باید به حساب مشتری متصل باشد.')}
  const visibleOrders=orders.filter(item=>filter==='active'?['فعال','در معرض دیرکرد'].includes(item.status):filter==='failed'?item.status==='ناموفق':item.status==='تحویل‌شده')
  const query=search.trim().toLocaleLowerCase()
  const filteredDemoOrders=visibleOrders.filter(item=>!query||`${item.id} ${item.route} ${item.detail} ${item.status}`.toLocaleLowerCase().includes(query))
  const filteredDemoNotifications=demoNotifications.filter(item=>notificationFilter==='all'||item.kind===notificationFilter)
  return <main className="mobile-shell demo-shell">
    <header className="app-header"><div className="brand-mark" aria-hidden="true">س</div><div className="header-copy"><strong>{headerTitle}</strong><span>{headerSubtitle}</span></div></header>
    <section className="page-content demo-page" aria-live="polite">
      {screen!=='detail'&&screen!=='exception'&&screen!=='review'&&screen!=='create'&&<button className="demo-branch-chip" onClick={()=>setScreen('more')}>ونک <span>⌄</span></button>}
      {toast&&<div className="feedback success" role="status">{toast}<button onClick={()=>setToast('')} aria-label="بستن پیام">×</button></div>}
      {screen==='home'&&<>
        <div className="section-head compact demo-page-heading"><h1>امروز چه چیزی نیاز به توجه دارد؟</h1></div>
        <button className="risk-card demo-risk" onClick={()=>openDetail('۸۴۲۱')}><span className="risk-icon">!</span><span><strong>یک ارسال در معرض دیرکرد</strong><small>سفارش ۸۴۲۱ • تعهد تا ۱۸:۴۵</small></span><b>‹</b></button>
        <div className="section-head compact demo-section-title"><h2>ارسال‌های فعال</h2></div>
        <div className="summary-card demo-summary"><div><b>۴</b><span>در حال اجرا</span></div><i/><div><b className="warning-number">۱</b><span>نیازمند توجه</span></div></div>
        <button className="primary-button full" onClick={()=>setScreen('create')}>ثبت ارسال جدید</button>
        <div className="section-head compact demo-section-title"><h2>آخرین ارسال‌ها</h2></div>
        <div className="delivery-list demo-delivery-list">{orders.slice(0,2).map(item=><DemoOrderCard key={item.id} item={item} onClick={()=>openDetail(item.id)}/>)}</div>
      </>}
      {screen==='deliveries'&&<>
        <div className="section-head compact demo-page-heading"><h1>ارسال‌ها</h1><span className="eyebrow">همه شعب</span></div>
        <div className="filter-tabs" role="tablist">{([['active','فعال'],['completed','تکمیل‌شده'],['failed','ناموفق']] as const).map(([key,label])=><button role="tab" aria-selected={filter===key} className={filter===key?'selected':''} key={key} onClick={()=>setFilter(key)}>{label}</button>)}</div>
        <label className="search-box"><span>⌕</span><input aria-label="جست‌وجوی سفارش" placeholder="جست‌وجوی شماره یا نشانی" value={search} onChange={event=>setSearch(event.target.value)} /></label>
        <div className="delivery-list demo-delivery-list">{filteredDemoOrders.map(item=><DemoOrderCard key={item.id} item={item} onClick={()=>openDetail(item.id)}/>)}{!filteredDemoOrders.length&&<EmptyState title="ارسالی پیدا نشد" body="شماره سفارش یا نشانی دیگری را جست‌وجو کنید."/>}</div>
      </>}
      {screen==='detail'&&<>
        <div className="section-head compact demo-page-heading"><h1>جزئیات ارسال</h1><span className={`status-badge ${!confirmed&&['۸۴۲۱','۸۴۱۲'].includes(order)?'warning':''}`}>{confirmed?'در انتظار تخصیص پیک':order==='۸۴۱۸'?'تحویل‌شده':order==='۸۴۱۲'?'ناموفق':'در معرض دیرکرد'}</span></div>
        <section className="demo-detail-promise"><span>{confirmed?'پیشنهاد تحویل':order==='۸۴۱۸'?'زمان تحویل':order==='۸۴۱۲'?'وضعیت ارسال':'تعهد تحویل'}</span><strong>{confirmed?'۱۹:۳۵':order==='۸۴۱۸'?'۱۹:۵۸':order==='۸۴۱۲'?'ناموفق':'۱۸:۴۵'}</strong><small>{confirmed?'زمان پیشنهادی مونو؛ نمونهٔ نمایشی':order==='۸۴۱۸'?'تحویل به گیرنده انجام شد':order==='۸۴۱۲'?'ارسال لغو شد؛ نیاز به بررسی دارد':'زمان فعلی برآوردی: ۱۸:۵۷'}</small></section>
        <section className="demo-detail-card"><h2>وضعیت</h2><div className="demo-timeline">{confirmed?<><div className="done"><i/><span><strong>درخواست ارسال ثبت شد</strong><small>در محیط نمایشی</small></span></div><div><i/><span><strong>در انتظار تخصیص پیک</strong><small>پس از اتصال به حساب مشتری</small></span></div><div><i/><span><strong>تحویل به پیک</strong><small>پس از تخصیص</small></span></div></>:order==='۸۴۱۸'?<><div className="done"><i/><span><strong>درخواست ثبت شد</strong><small>۱۸:۲۰</small></span></div><div className="done"><i/><span><strong>تحویل به پیک</strong><small>۱۹:۱۰</small></span></div><div className="done"><i/><span><strong>تحویل به گیرنده</strong><small>۱۹:۵۸</small></span></div></>:order==='۸۴۱۲'?<><div className="done"><i/><span><strong>درخواست ثبت شد</strong><small>۱۸:۰۵</small></span></div><div className="late"><i/><span><strong>ارسال ناموفق شد</strong><small>۱۸:۲۰</small></span></div></>:<><div className="done"><i/><span><strong>پیک تخصیص داده شد</strong><small>۱۷:۵۸</small></span></div><div className="done"><i/><span><strong>تحویل از شعبه انجام شد</strong><small>۱۸:۱۲</small></span></div><div className="done"><i/><span><strong>در مسیر مقصد</strong><small>۱۸:۲۴</small></span></div><div className="late"><i/><span><strong>ریسک دیرکرد شناسایی شد</strong><small>۱۸:۳۷</small></span></div></>}</div></section>
        <section className="demo-detail-card demo-addresses"><div><small>مبدأ</small><strong>شعبه ونک</strong></div><div><small>مقصد</small><strong>{confirmed?demoDestination:order==='۸۴۱۸'?'میرداماد':order==='۸۴۱۲'?'تهران‌پارس':order==='۸۴۱۵'?'سعادت‌آباد':order==='۸۴۱۰'?'شهرک غرب':'یوسف‌آباد، خیابان شصت‌وچهارم'}</strong></div>{confirmed&&<div><small>گیرنده</small><strong>{demoRecipient}</strong></div>}</section>
        <div className="demo-detail-actions"><button className="secondary-button" onClick={()=>setToast('راه‌های تماس با پشتیبانی در این پیش‌نمایش نمایشی‌اند.')}>پشتیبانی</button><button className="primary-button" onClick={()=>order==='۸۴۲۱'?setScreen('exception'):setToast('برای این ارسال نمونه، استثنای فعالی ثبت نشده است.')}>مشاهده اقدام‌ها</button></div>
        {confirmed&&<p className="demo-confirmed-note">درخواست نمایشی تأیید شد؛ هنوز به سرور ثبت نشده است.</p>}
      </>}
      {screen==='exception'&&<>
        <section className="demo-exception-overview"><span className="demo-exception-badge">نیازمند توجه</span><strong>۱۲ دقیقه ریسک دیرکرد</strong><small>تعهد: ۱۸:۴۵ • برآورد فعلی: ۱۸:۵۷</small></section>
        <section className="demo-exception-section"><h2>چه اتفاقی افتاده؟</h2><div className="demo-exception-card"><strong>کندی مسیر و تراکم ترافیکی</strong><p>مونو مسیر و زمان تحویل را دوباره ارزیابی کرده و وضعیت را تحت پایش نگه داشته است.</p></div></section>
        <section className="demo-exception-section"><h2>اقدام مونو</h2><div className="demo-exception-card demo-exception-action"><strong>پایش مسیر جایگزین و کنترل تعهد</strong><p>اگر نیاز به مداخله باشد، تیم عملیات اقدام می‌کند.</p></div></section>
        <section className="demo-exception-section demo-exception-user"><h2>اقدام شما</h2><div className="demo-exception-actions"><button className="secondary-button" onClick={()=>setToast('راه‌های تماس با پشتیبانی در این پیش‌نمایش نمایشی‌اند.')}>تماس با پشتیبانی</button><button className="primary-button" onClick={()=>{setScreen('detail');setToast('')}}>بازگشت به ارسال</button></div></section>
      </>}
      {screen==='create'&&<>
        <div className="section-head compact demo-page-heading"><div><span className="eyebrow">۱ از ۲</span><h1>ثبت ارسال جدید</h1></div><span className="step-pill">اطلاعات ارسال</span></div>
        <form className="stack-form create-form demo-create-form" onSubmit={event=>{event.preventDefault();review()}}>
          <section className="form-section"><b>مبدأ</b><label>شعبه مبدأ<input value="شعبه ونک" readOnly /></label><label>نام گیرنده<input value={demoRecipient} onChange={event=>setDemoRecipient(event.target.value)} /></label><label>شماره تماس<input inputMode="tel" value={demoPhone} onChange={event=>setDemoPhone(event.target.value)} /></label></section>
          <section className="form-section"><b>مقصد</b><label>نشانی مقصد<input value={demoDestination} onChange={event=>setDemoDestination(event.target.value)} /></label></section>
          <section className="form-section"><b>بسته</b><label>نوع بسته<input value="بسته معمولی • ۲ کیلوگرم" readOnly /></label><label>زمان آمادگی<input value="همین حالا" readOnly /></label></section>
          <button className="primary-button" type="submit">بررسی هزینه و زمان</button>
        </form>
      </>}
      {screen==='review'&&<>
        <div className="section-head compact demo-page-heading"><h1>پیشنهاد مونو</h1><span className="status-badge">قابل اجرا</span></div>
        <section className="demo-offer"><div><span>تعهد تحویل</span><strong>تا ۱۹:۳۵</strong></div><div><span>هزینه ارسال</span><strong>۴۲٬۵۰۰ تومان</strong></div></section>
        <section className="demo-detail-card demo-review-summary"><h2>خلاصه ارسال</h2><div><span>مبدأ</span><strong>شعبه ونک</strong></div><div><span>مقصد</span><strong>{demoDestination}</strong></div><div><span>گیرنده</span><strong>{demoRecipient}</strong></div><div><span>تعهد</span><strong>فقط پس از تأیید ثبت می‌شود.</strong></div></section>
        <div className="demo-review-actions"><button className="secondary-button" onClick={()=>setScreen('create')}>بازگشت</button><button className="primary-button" onClick={confirm}>تأیید و ثبت ارسال</button></div>
      </>}
      {screen==='notifications'&&<>
        <div className="section-head compact demo-page-heading"><h1>اعلان‌ها</h1></div>
        <div className="demo-notification-filters" role="tablist" aria-label="دسته‌بندی اعلان‌ها">{([['all','همه'],['action','نیازمند اقدام'],['info','اطلاع‌رسانی']] as const).map(([key,label])=><button key={key} role="tab" aria-selected={notificationFilter===key} className={notificationFilter===key?'selected':''} onClick={()=>setNotificationFilter(key)}>{label}</button>)}</div>
        {(['today','yesterday'] as const).map(day=>{const items=filteredDemoNotifications.filter(item=>item.day===day);return items.length>0&&<section className="demo-notification-group" key={day} aria-label={day==='today'?'امروز':'دیروز'}><h2>{day==='today'?'امروز':'دیروز'}</h2><div className="demo-notification-list">{items.map(item=>{const isRead=readNotificationIds.includes(item.id);return <button key={item.id} className="demo-notification-card" aria-label={item.title} onClick={()=>openDemoNotification(item)}><span className={'demo-notification-dot '+(isRead?'read':'')} aria-label={isRead?'خوانده شده':'خوانده نشده'}/><span className="demo-notification-copy"><strong>{item.title}</strong><small>{item.detail}</small></span><span className="demo-notification-arrow" aria-hidden="true">‹</span></button>})}</div></section>})}
        {!filteredDemoNotifications.length&&<EmptyState title="اعلانی در این دسته نیست" body="با انتخاب دستهٔ دیگر، اعلان‌های بیشتری می‌بینید."/>}
      </>}
      {screen==='more'&&<><div className="section-head compact demo-page-heading"><h1>بیشتر</h1></div><div className="settings-card"><p><span>شعبهٔ انتخاب‌شده</span><strong>شعبه ونک</strong></p><button className="branch-option"><span>شعبه ونک</span><span>✓</span></button><small>نسخهٔ نمایشی با داده‌های نمونه</small></div></>}
    </section>
    <nav className="bottom-nav demo-bottom-nav" aria-label="ناوبری اصلی">{nav.map(([id,title,icon])=><button key={id} className={(screen===id?'active ':'')+(id==='create'?'create-tab':'')} onClick={()=>{setScreen(id);setToast('')}} aria-current={screen===id?'page':undefined}><DemoNavIcon name={icon}/><small>{title}</small></button>)}</nav>
  </main>
}

function DemoOrderCard({item,onClick}:{item:{id:string;route:string;detail:string;status:string;warning:boolean};onClick:()=>void}) {
  return <button className="delivery-card demo-order-card" onClick={onClick}><span className="delivery-symbol">↙</span><span className="delivery-main"><strong>سفارش {item.id}</strong><small>{item.route}</small><small>{item.detail}</small></span><span className={'status-badge '+(item.warning?'warning':'')}>{item.status}</span></button>
}

function DemoNavIcon({name}:{name:string}) {
  if(name==='plus') return <span className="demo-plus-icon">＋</span>
  const paths:Record<string,ReactNode>={
    home:<><path d="m3 10 9-7 9 7"/><path d="M5 9v11h14V9M9 20v-6h6v6"/></>,
    list:<><rect x="4" y="4" width="16" height="16" rx="2"/><path d="M8 8h8M8 12h8M8 16h5"/></>,
    bell:<><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9"/><path d="M10 21h4"/></>,
    more:<><circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/></>,
  }
  return <svg className="demo-nav-svg" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">{paths[name]}</svg>
}

function DeliveryCard({ item, onClick }: { item: Delivery; onClick: () => void }) {
  const status = item.mission_status || item.status
  const risky = /risk|late|delay/i.test(`${item.status} ${item.mission_status}`)
  return <button className="delivery-card" onClick={onClick}><span className="delivery-symbol">↙</span><span className="delivery-main"><strong>سفارش {item.external_reference || item.delivery_request_id.slice(-6)}</strong><small>ثبت {formatDate(item.created_at)}</small></span><span className={`status-badge ${risky ? 'warning' : ''}`}>{risky ? 'نیازمند توجه' : statusText(status)}</span><span className="card-arrow">‹</span></button>
}

function EmptyState({ title, body }: { title: string; body: string }) {
  return <div className="empty-state"><span className="empty-icon">⌂</span><strong>{title}</strong><p>{body}</p></div>
}
