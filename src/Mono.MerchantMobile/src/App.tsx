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
type View = 'home' | 'deliveries' | 'create' | 'notifications' | 'finance' | 'profile' | 'settings' | 'support' | 'more'
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

type DemoMobileScreen = 'home' | 'deliveries' | 'detail' | 'exception' | 'branch' | 'create' | 'review' | 'notifications' | 'finance' | 'profile' | 'settings' | 'notificationSettings' | 'security' | 'support' | 'more'

function MerchantMobileDemo() {
  const [screen,setScreen]=useState<DemoMobileScreen>('home')
  const [filter,setFilter]=useState<'active'|'completed'|'failed'>('active')
  const [search,setSearch]=useState('')
  const [order,setOrder]=useState('۸۴۲۱')
  const [activeBranch,setActiveBranch]=useState('ونک')
  const [confirmed,setConfirmed]=useState(false)
  const [toast,setToast]=useState('')
  const [notificationPrefs,setNotificationPrefs]=useState({risk:true,assignment:true,pickup:true,completion:true,finance:false})
  const [notificationFilter,setNotificationFilter]=useState<'all'|'action'|'info'>('all')
  const [readNotificationIds,setReadNotificationIds]=useState<string[]>([])
  const [demoRecipient,setDemoRecipient]=useState('مریم اکبری')
  const [demoPhone,setDemoPhone]=useState('۰۹۱۲ ۱۲۳ ۴۵۶۷')
  const [demoDestination,setDemoDestination]=useState('سعادت‌آباد، بلوار دریا')
  const nav:[DemoMobileScreen,string,string][]=[['more','بیشتر','more'],['notifications','اعلان‌ها','bell'],['create','ثبت','plus'],['deliveries','ارسال‌ها','list'],['home','خانه','home']]
  const allOrders=[
    {id:'۸۴۳۰',branchName:'ونک',route:'ونک ← یوسف‌آباد',destination:'یوسف‌آباد',promiseTime:'۱۹:۲۰',detail:'تحویل تا ۱۹:۲۰',status:'فعال',warning:false},
    {id:'۸۴۲۱',branchName:'ونک',route:'ونک ← جردن',destination:'جردن',promiseTime:'۱۸:۴۵',detail:'تعهد ۱۸:۴۵ • ۱۲ دقیقه ریسک',status:'در معرض دیرکرد',warning:true},
    {id:'۸۴۱۸',branchName:'ونک',route:'ونک ← میرداماد',destination:'میرداماد',promiseTime:'۱۹:۵۸',detail:'تحویل شد ۱۹:۵۸',status:'تحویل‌شده',warning:false},
    {id:'۸۴۱۵',branchName:'ونک',route:'ونک ← سعادت‌آباد',destination:'سعادت‌آباد',promiseTime:'۲۰:۳۰',detail:'تحویل تا ۲۰:۳۰',status:'فعال',warning:false},
    {id:'۸۴۱۲',branchName:'ونک',route:'ونک ← تهران‌پارس',destination:'تهران‌پارس',promiseTime:'ناموفق',detail:'ارسال لغو شد • ۱۸:۲۰',status:'ناموفق',warning:true},
    {id:'۸۴۱۰',branchName:'ونک',route:'ونک ← شهرک غرب',destination:'شهرک غرب',promiseTime:'۲۰:۴۰',detail:'تحویل تا ۲۰:۴۰',status:'فعال',warning:false},
    {id:'۸۴۵۲',branchName:'جردن',route:'جردن ← قلهک',destination:'قلهک',promiseTime:'۲۰:۰۵',detail:'تحویل تا ۲۰:۰۵',status:'فعال',warning:false},
    {id:'۸۴۵۱',branchName:'جردن',route:'جردن ← میرداماد',destination:'میرداماد',promiseTime:'۲۰:۲۰',detail:'تحویل تا ۲۰:۲۰',status:'فعال',warning:false},
    {id:'۸۴۴۸',branchName:'پاسداران',route:'پاسداران ← هروی',destination:'هروی',promiseTime:'۱۹:۵۰',detail:'تحویل تا ۱۹:۵۰',status:'فعال',warning:false},
  ]
  const orders=allOrders.filter(item=>item.branchName===activeBranch)
  const selectedOrder=allOrders.find(item=>item.id===order)

  const demoNotifications=[
    {id:'risk-8421',day:'today',kind:'action' as const,title:'ریسک دیرکرد برای سفارش ۸۴۲۱',detail:'تعهد ۱۸:۴۵ • شعبه ونک',orderId:'۸۴۲۱'},
    {id:'assigned-8430',day:'today',kind:'info' as const,title:'سفارش ۸۴۳۰ به پیک تخصیص داده شد',detail:'تحویل تا ۱۹:۲۰',orderId:'۸۴۳۰'},
    {id:'handoff-8418',day:'today',kind:'info' as const,title:'سفارش ۸۴۱۸ از شعبه تحویل شد',detail:'در مسیر مقصد',orderId:'۸۴۱۸'},
    {id:'delivered-8398',day:'yesterday',kind:'info' as const,title:'سفارش ۸۳۹۸ تحویل شد',detail:'گیرنده: کیانا مرادی',orderId:'۸۳۹۸'},
    {id:'invoice-1',day:'yesterday',kind:'info' as const,title:'صورتحساب هفتگی صادر شد',detail:'دوره ۲۲ تا ۲۸ شهریور'},
  ]
  const headerTitle=screen==='home'?'سلام، فروشگاه سپهر':screen==='deliveries'?'ارسال‌ها':screen==='detail'?'سفارش '+order:screen==='create'?'ثبت ارسال':screen==='review'?'بررسی و تأیید':screen==='exception'?'استثنای ارسال':screen==='branch'?'انتخاب شعبه':screen==='notifications'?'اعلان‌ها':screen==='finance'?'مالی':screen==='profile'?'پروفایل':screen==='settings'?'تنظیمات':screen==='notificationSettings'?'تنظیمات اعلان‌ها':screen==='security'?'امنیت و نشست‌ها':screen==='support'?'پشتیبانی':'بیشتر'
  const headerSubtitle=screen==='exception'||screen==='support'?'سفارش '+order:screen==='profile'?'حساب کاربری':screen==='settings'?'حساب و برنامه':screen==='notificationSettings'?'عملیاتی':screen==='security'?'حساب کاربری':screen==='branch'||screen==='finance'?'فروشگاه سپهر':`شعبه ${screen==='detail'?(selectedOrder?.branchName||activeBranch):activeBranch}`
  function openDetail(id:string){setOrder(id);setScreen('detail');setToast('')}
  function openDemoNotification(item:typeof demoNotifications[number]) {
    setReadNotificationIds(ids=>ids.includes(item.id)?ids:[...ids,item.id])
    if(item.orderId) openDetail(item.orderId)
    else setToast('این صورتحساب فقط نمونهٔ نمایشی است و دادهٔ مالی واقعی نیست.')
  }
  function review(){setScreen('review');setToast('')}
  function confirm(){setConfirmed(true);setOrder('۸۴۳۱');setScreen('detail');setToast('درخواست نمایشی ثبت شد؛ در نسخهٔ واقعی باید به حساب مشتری متصل باشد.')}
  const visibleOrders=allOrders.filter(item=>filter==='active'?['فعال','در معرض دیرکرد'].includes(item.status):filter==='failed'?item.status==='ناموفق':item.status==='تحویل‌شده')
  const activeCount=orders.filter(item=>['فعال','در معرض دیرکرد'].includes(item.status)).length
  const attentionCount=orders.filter(item=>item.status==='در معرض دیرکرد').length
  const detailStatus=confirmed?'در انتظار تخصیص پیک':selectedOrder?.status||(order==='۸۴۱۸'?'تحویل‌شده':order==='۸۴۱۲'?'ناموفق':'در معرض دیرکرد')
  const detailPromise=confirmed?'۱۹:۳۵':selectedOrder?.promiseTime||(order==='۸۴۱۸'?'۱۹:۵۸':order==='۸۴۱۲'?'ناموفق':'۱۸:۴۵')
  const detailDestination=confirmed?demoDestination:selectedOrder?.destination||(order==='۸۴۱۸'?'میرداماد':order==='۸۴۱۲'?'تهران‌پارس':order==='۸۴۱۵'?'سعادت‌آباد':order==='۸۴۱۰'?'شهرک غرب':'یوسف‌آباد، خیابان شصت‌وچهارم')
  const query=search.trim().toLocaleLowerCase()
  const filteredDemoOrders=visibleOrders.filter(item=>!query||`${item.id} ${item.route} ${item.detail} ${item.status}`.toLocaleLowerCase().includes(query))
  const filteredDemoNotifications=demoNotifications.filter(item=>notificationFilter==='all'||item.kind===notificationFilter)
  return <main className="mobile-shell demo-shell">
    <header className="app-header"><div className="brand-mark" aria-hidden="true">س</div><div className="header-copy"><strong>{headerTitle}</strong><span>{headerSubtitle}</span></div></header>
    <section className="page-content demo-page" aria-live="polite">
      {screen!=='detail'&&screen!=='exception'&&screen!=='branch'&&screen!=='review'&&screen!=='create'&&screen!=='finance'&&screen!=='profile'&&screen!=='settings'&&screen!=='notificationSettings'&&screen!=='security'&&screen!=='support'&&<button className="demo-branch-chip" onClick={()=>setScreen('branch')}>{activeBranch} <span>⌄</span></button>}
      {toast&&<div className="feedback success" role="status">{toast}<button onClick={()=>setToast('')} aria-label="بستن پیام">×</button></div>}
      {screen==='home'&&<>
        <div className="section-head compact demo-page-heading"><h1>امروز چه چیزی نیاز به توجه دارد؟</h1></div>
        {attentionCount>0&&<button className="risk-card demo-risk" onClick={()=>openDetail('۸۴۲۱')}><span className="risk-icon">!</span><span><strong>یک ارسال در معرض دیرکرد</strong><small>سفارش ۸۴۲۱ • تعهد تا ۱۸:۴۵</small></span><b>‹</b></button>}
        <div className="section-head compact demo-section-title"><h2>ارسال‌های فعال</h2></div>
        <div className="summary-card demo-summary"><div><b>{fa.format(activeCount)}</b><span>در حال اجرا</span></div><i/><div><b className="warning-number">{fa.format(attentionCount)}</b><span>نیازمند توجه</span></div></div>
        <button className="primary-button full" onClick={()=>setScreen('create')}>ثبت ارسال جدید</button>
        <div className="section-head compact demo-section-title"><h2>آخرین ارسال‌ها</h2></div>
        <div className="delivery-list demo-delivery-list">{orders.slice(0,2).map(item=><DemoOrderCard key={item.id} item={item} onClick={()=>openDetail(item.id)}/>)}{!orders.length&&<EmptyState title="ارسالی برای این شعبه نیست" body="از شعبهٔ دیگری انتخاب کنید یا ارسال جدید ثبت کنید."/>}</div>
      </>}
      {screen==='deliveries'&&<>
        <div className="section-head compact demo-page-heading"><h1>ارسال‌ها</h1><span className="eyebrow">همه شعب</span></div>
        <div className="filter-tabs" role="tablist">{([['active','فعال'],['completed','تکمیل‌شده'],['failed','ناموفق']] as const).map(([key,label])=><button role="tab" aria-selected={filter===key} className={filter===key?'selected':''} key={key} onClick={()=>setFilter(key)}>{label}</button>)}</div>
        <label className="search-box"><span>⌕</span><input aria-label="جست‌وجوی سفارش" placeholder="جست‌وجوی شماره یا نشانی" value={search} onChange={event=>setSearch(event.target.value)} /></label>
        <div className="delivery-list demo-delivery-list">{filteredDemoOrders.map(item=><DemoOrderCard key={item.id} item={item} onClick={()=>openDetail(item.id)}/>)}{!filteredDemoOrders.length&&<EmptyState title="ارسالی پیدا نشد" body="شماره سفارش یا نشانی دیگری را جست‌وجو کنید."/>}</div>
      </>}
      {screen==='detail'&&<>
        <div className="section-head compact demo-page-heading"><h1>جزئیات ارسال</h1><span className={`status-badge ${!confirmed&&detailStatus!=='فعال'?'warning':''}`}>{detailStatus}</span></div>
        <section className="demo-detail-promise"><span>{confirmed?'پیشنهاد تحویل':detailStatus==='تحویل‌شده'?'زمان تحویل':detailStatus==='ناموفق'?'وضعیت ارسال':'تعهد تحویل'}</span><strong>{detailPromise}</strong><small>{confirmed?'زمان پیشنهادی مونو؛ نمونهٔ نمایشی':detailStatus==='تحویل‌شده'?'تحویل به گیرنده انجام شد':detailStatus==='ناموفق'?'ارسال لغو شد؛ نیاز به بررسی دارد':detailStatus==='فعال'?'ارسال در جریان؛ دادهٔ نمایشی':'زمان فعلی برآوردی: ۱۸:۵۷'}</small></section>
        <section className="demo-detail-card"><h2>وضعیت</h2><div className="demo-timeline">{confirmed?<><div className="done"><i/><span><strong>درخواست ارسال ثبت شد</strong><small>در محیط نمایشی</small></span></div><div><i/><span><strong>در انتظار تخصیص پیک</strong><small>پس از اتصال به حساب مشتری</small></span></div><div><i/><span><strong>تحویل به پیک</strong><small>پس از تخصیص</small></span></div></>:detailStatus==='تحویل‌شده'?<><div className="done"><i/><span><strong>درخواست ثبت شد</strong><small>۱۸:۲۰</small></span></div><div className="done"><i/><span><strong>تحویل به پیک</strong><small>۱۹:۱۰</small></span></div><div className="done"><i/><span><strong>تحویل به گیرنده</strong><small>۱۹:۵۸</small></span></div></>:detailStatus==='ناموفق'?<><div className="done"><i/><span><strong>درخواست ثبت شد</strong><small>۱۸:۰۵</small></span></div><div className="late"><i/><span><strong>ارسال ناموفق شد</strong><small>۱۸:۲۰</small></span></div></>:detailStatus==='فعال'?<><div className="done"><i/><span><strong>درخواست ارسال ثبت شد</strong><small>در محیط نمایشی</small></span></div><div className="done"><i/><span><strong>پیک تخصیص داده شد</strong><small>در حال اجرا</small></span></div></>:<><div className="done"><i/><span><strong>پیک تخصیص داده شد</strong><small>۱۷:۵۸</small></span></div><div className="done"><i/><span><strong>تحویل از شعبه انجام شد</strong><small>۱۸:۱۲</small></span></div><div className="done"><i/><span><strong>در مسیر مقصد</strong><small>۱۸:۲۴</small></span></div><div className="late"><i/><span><strong>ریسک دیرکرد شناسایی شد</strong><small>۱۸:۳۷</small></span></div></>}</div></section>
        <section className="demo-detail-card demo-addresses"><div><small>مبدأ</small><strong>شعبه {confirmed?activeBranch:selectedOrder?.branchName||activeBranch}</strong></div><div><small>مقصد</small><strong>{detailDestination}</strong></div>{confirmed&&<div><small>گیرنده</small><strong>{demoRecipient}</strong></div>}</section>
        <div className="demo-detail-actions"><button className="secondary-button" onClick={()=>{setScreen('support');setToast('')}}>پشتیبانی</button><button className="primary-button" onClick={()=>order==='۸۴۲۱'?setScreen('exception'):setToast('برای این ارسال نمونه، استثنای فعالی ثبت نشده است.')}>مشاهده اقدام‌ها</button></div>
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
          <section className="form-section"><b>مبدأ</b><label>شعبه مبدأ<input value={`شعبه ${activeBranch}`} readOnly /></label><label>نام گیرنده<input value={demoRecipient} onChange={event=>setDemoRecipient(event.target.value)} /></label><label>شماره تماس<input inputMode="tel" value={demoPhone} onChange={event=>setDemoPhone(event.target.value)} /></label></section>
          <section className="form-section"><b>مقصد</b><label>نشانی مقصد<input value={demoDestination} onChange={event=>setDemoDestination(event.target.value)} /></label></section>
          <section className="form-section"><b>بسته</b><label>نوع بسته<input value="بسته معمولی • ۲ کیلوگرم" readOnly /></label><label>زمان آمادگی<input value="همین حالا" readOnly /></label></section>
          <button className="primary-button" type="submit">بررسی هزینه و زمان</button>
        </form>
      </>}
      {screen==='review'&&<>
        <div className="section-head compact demo-page-heading"><h1>پیشنهاد مونو</h1><span className="status-badge">قابل اجرا</span></div>
        <section className="demo-offer"><div><span>تعهد تحویل</span><strong>تا ۱۹:۳۵</strong></div><div><span>هزینه ارسال</span><strong>۴۲٬۵۰۰ تومان</strong></div></section>
        <section className="demo-detail-card demo-review-summary"><h2>خلاصه ارسال</h2><div><span>مبدأ</span><strong>شعبه {activeBranch}</strong></div><div><span>مقصد</span><strong>{demoDestination}</strong></div><div><span>گیرنده</span><strong>{demoRecipient}</strong></div><div><span>تعهد</span><strong>فقط پس از تأیید ثبت می‌شود.</strong></div></section>
        <div className="demo-review-actions"><button className="secondary-button" onClick={()=>setScreen('create')}>بازگشت</button><button className="primary-button" onClick={confirm}>تأیید و ثبت ارسال</button></div>
      </>}
      {screen==='branch'&&<>
        <div className="section-head compact demo-page-heading"><h1>شعبه فعال</h1></div>
        <button className="demo-branch-card selected" aria-label={`شعبه ${activeBranch}، انتخاب‌شده`} onClick={()=>{setScreen('home');setToast('')}}><span className="demo-branch-radio"/><span className="demo-branch-copy"><strong>شعبه {activeBranch}</strong><small>آماده ارسال • {activeBranch==='ونک'?'۴':activeBranch==='جردن'?'۲':activeBranch==='پاسداران'?'۱':'۰'} ارسال فعال</small></span></button>
        <div className="section-head compact demo-branch-other-title"><h2>شعب دیگر</h2></div>
        <div className="demo-branch-list">{[{name:'جردن',count:'۲',available:true},{name:'میرداماد',count:'۰',available:true},{name:'پونک',count:'۰',available:false},{name:'پاسداران',count:'۱',available:true}].filter(branch=>branch.name!==activeBranch).map(branch=><button className="demo-branch-card" key={branch.name} disabled={!branch.available} aria-label={`${branch.name}؛ ${branch.available?`آماده ارسال • ${branch.count} ارسال فعال`:'موقتاً غیرفعال'}`} onClick={()=>{setActiveBranch(branch.name);setScreen('home');setToast('')}}><span className="demo-branch-copy"><strong>{branch.name}</strong><small>{branch.available?`آماده ارسال • ${branch.count} ارسال فعال`:'موقتاً غیرفعال'}</small></span><span className="demo-branch-arrow" aria-hidden="true">‹</span></button>)}</div>
        <p className="demo-branch-note">ارسال جدید از شعبهٔ انتخاب‌شده ثبت می‌شود.</p>
      </>}
      {screen==='security'&&<>
        <div className="section-head compact demo-page-heading"><h1>امنیت</h1></div>
        <section className="demo-security-device" aria-label="این دستگاه">
          <div className="demo-security-copy"><strong>این دستگاه</strong><span>آیفون • تهران</span><small>آخرین فعالیت: همین حالا</small></div>
          <span className="demo-security-active">فعال</span>
        </section>
        <h2 className="demo-security-section-title">نشست‌های دیگر</h2>
        <button className="demo-security-session" onClick={()=>setToast('نشست مرورگر فقط نمونه است و به حساب واقعی وصل نیست.')}><span><strong>مرورگر وب</strong><small>تهران • امروز ۱۲:۴۵</small></span><b aria-hidden="true">‹</b></button>
        <h2 className="demo-security-section-title demo-security-access-title">ورود و امنیت</h2>
        <div className="demo-settings-list">
          <button className="demo-settings-item" onClick={()=>setToast('تغییر رمز عبور در نسخهٔ نمایشی انجام نمی‌شود.')}><span className="demo-settings-icon">⌑</span><span><strong>تغییر رمز عبور</strong><small>به‌روزرسانی رمز حساب</small></span><b>‹</b></button>
          <button className="demo-settings-item" onClick={()=>setToast('خروج از نشست‌های دیگر در نسخهٔ نمایشی انجام نمی‌شود.')}><span className="demo-settings-icon">↗</span><span><strong>خروج از همه دستگاه‌ها</strong><small>به‌جز این دستگاه</small></span><b>‹</b></button>
        </div>
        <button className="demo-security-signout" onClick={()=>setToast('خروج از حساب در نسخهٔ نمایشی انجام نمی‌شود.')}>خروج از حساب</button>
        <small className="demo-settings-demo-label">نشست‌ها و اطلاعات این صفحه نمونهٔ نمایشی هستند.</small>
      </>}
      {screen==='notificationSettings'&&<>
        <div className="section-head compact demo-page-heading"><h1>چه اعلان‌هایی دریافت کنید؟</h1></div>
        <div className="demo-notification-preferences" aria-label="ترجیحات اعلان‌های عملیاتی">
          {([
            ['risk','ریسک دیرکرد و استثنا','همیشه برای موارد نیازمند توجه'],
            ['assignment','تخصیص پیک','وقتی پیک به ارسال تخصیص داده شد'],
            ['pickup','تحویل از شعبه','وقتی سفارش از مبدأ تحویل گرفته شد'],
            ['completion','تحویل نهایی','پس از تحویل موفق یا ناموفق'],
            ['finance','مالی','صورتحساب و اعلان‌های مالی'],
          ] as const).map(([key,title,description])=><button type="button" role="switch" aria-checked={notificationPrefs[key]} aria-label={title} className="demo-notification-preference" key={key} onClick={()=>setNotificationPrefs(prefs=>({...prefs,[key]:!prefs[key]}))}><span className="demo-notification-preference-copy"><strong>{title}</strong><small>{description}</small></span><span className={'demo-toggle '+(notificationPrefs[key]?'on':'')} aria-hidden="true"><i/></span></button>)}
        </div>
        <div className="demo-critical-notice">اعلان‌های بحرانی عملیاتی حتی در حالت کم‌صدا نمایش داده می‌شوند.</div>
        <small className="demo-settings-demo-label">ترجیحات در این نسخهٔ نمایشی ذخیره نمی‌شوند.</small>
      </>}
      {screen==='support'&&<>
        <div className="section-head compact demo-page-heading"><h1>زمینه ارسال به‌صورت خودکار متصل شد</h1></div>
        <section className="demo-support-order" aria-label={`سفارش ${order}`}>
          <strong>سفارش {order}</strong>
          <span>{order==='۸۴۲۱'?'در معرض دیرکرد • تعهد ۱۸:۴۵':selectedOrder?.detail||'در حال ارسال'}</span>
        </section>
        <fieldset className="demo-support-topics">
          <legend>موضوع درخواست</legend>
          {['تاخیر در تحویل','تغییر اطلاعات گیرنده','مشکل در لغو','موضوع دیگر'].map(topic=><label className="demo-support-topic" key={topic}><input type="radio" name="support-topic" value={topic} defaultChecked={topic==='تاخیر در تحویل'}/><span>{topic}</span></label>)}
        </fieldset>
        <label className="demo-support-description">توضیح کوتاه<textarea aria-label="توضیح کوتاه" placeholder="در صورت نیاز توضیح بیشتری اضافه کنید…"/></label>
        <button className="demo-support-submit" onClick={()=>setToast('درخواست پشتیبانی نمایشی ثبت شد؛ هنوز به سامانهٔ پشتیبانی وصل نیست.')}>ثبت درخواست پشتیبانی</button>
        <small className="demo-support-demo-label">ارسال درخواست در این نسخه فقط نمایشی است.</small>
      </>}
      {screen==='notifications'&&<>
        <div className="section-head compact demo-page-heading"><h1>اعلان‌ها</h1></div>
        <div className="demo-notification-filters" role="tablist" aria-label="دسته‌بندی اعلان‌ها">{([['all','همه'],['action','نیازمند اقدام'],['info','اطلاع‌رسانی']] as const).map(([key,label])=><button key={key} role="tab" aria-selected={notificationFilter===key} className={notificationFilter===key?'selected':''} onClick={()=>setNotificationFilter(key)}>{label}</button>)}</div>
        {(['today','yesterday'] as const).map(day=>{const items=filteredDemoNotifications.filter(item=>item.day===day);return items.length>0&&<section className="demo-notification-group" key={day} aria-label={day==='today'?'امروز':'دیروز'}><h2>{day==='today'?'امروز':'دیروز'}</h2><div className="demo-notification-list">{items.map(item=>{const isRead=readNotificationIds.includes(item.id);return <button key={item.id} className="demo-notification-card" aria-label={item.title} onClick={()=>openDemoNotification(item)}><span className={'demo-notification-dot '+(isRead?'read':'')} aria-label={isRead?'خوانده شده':'خوانده نشده'}/><span className="demo-notification-copy"><strong>{item.title}</strong><small>{item.detail}</small></span><span className="demo-notification-arrow" aria-hidden="true">‹</span></button>})}</div></section>})}
        {!filteredDemoNotifications.length&&<EmptyState title="اعلانی در این دسته نیست" body="با انتخاب دستهٔ دیگر، اعلان‌های بیشتری می‌بینید."/>}
      </>}
      {screen==='more'&&<>
        <div className="section-head compact demo-page-heading"><h1>بیشتر</h1></div>
        <div className="demo-profile-card"><span className="demo-profile-avatar">س</span><span><strong>مهدی کاظمی</strong><small>مدیر حساب • فروشگاه سپهر</small></span><b>شعبه {activeBranch}</b></div>
        <div className="demo-more-list">
          <button className="demo-more-item" aria-label="پروفایل، مشخصات فردی و نقش" onClick={()=>setScreen('profile')}><span className="demo-more-icon">م</span><span><strong>پروفایل</strong><small>مشخصات فردی و نقش</small></span><b>‹</b></button>
          <button className="demo-more-item" onClick={()=>setScreen('branch')}><span className="demo-more-icon">⌂</span><span><strong>شعب</strong><small>مشاهده و تغییر شعبه فعال</small></span><b>‹</b></button>
          <button className="demo-more-item" aria-label="مالی، خلاصه هزینه‌ها و وضعیت تسویه" onClick={()=>setScreen('finance')}><span className="demo-more-icon">▤</span><span><strong>مالی</strong><small>خلاصه هزینه‌ها و وضعیت تسویه</small></span><b>‹</b></button>
          <button className="demo-more-item" onClick={()=>{setOrder('۸۴۲۱');setScreen('support');setToast('')}}><span className="demo-more-icon">؟</span><span><strong>پشتیبانی</strong><small>درخواست‌ها و ارتباط با پشتیبانی</small></span><b>‹</b></button>
          <button className="demo-more-item" onClick={()=>setScreen('settings')}><span className="demo-more-icon">⚙</span><span><strong>تنظیمات</strong><small>اعلان‌ها، امنیت و ترجیحات</small></span><b>‹</b></button>
          <div className="demo-more-web"><strong>تنظیمات سازمانی کامل</strong><small>مدیریت اعضا، اتصال‌ها و هویت بصری در پنل وب</small></div>
        </div>
      </>}
      {screen==='settings'&&<>
        <div className="section-head compact demo-page-heading"><h1>تنظیمات</h1></div>
        <h2 className="demo-settings-section-title">تنظیمات</h2>
        <div className="demo-settings-list">
          <button className="demo-settings-item" aria-label="اعلان‌ها، انتخاب اعلان‌های عملیاتی و مالی" onClick={()=>{setScreen('notificationSettings');setToast('')}}><span className="demo-settings-icon">♧</span><span><strong>اعلان‌ها</strong><small>انتخاب اعلان‌های عملیاتی و مالی</small></span><b>‹</b></button>
          <button className="demo-settings-item" aria-label={`شعبه پیش‌فرض، شعبه ${activeBranch}`} onClick={()=>setScreen('branch')}><span className="demo-settings-icon">⌂</span><span><strong>شعبه پیش‌فرض</strong><small>شعبه {activeBranch}</small></span><b>‹</b></button>
          <button className="demo-settings-item" aria-label="امنیت و نشست‌ها، دستگاه‌ها و خروج از نشست" onClick={()=>setScreen('security')}><span className="demo-settings-icon">▣</span><span><strong>امنیت و نشست‌ها</strong><small>دستگاه‌ها و خروج از نشست</small></span><b>‹</b></button>
          <button className="demo-settings-item" aria-label="ظاهر برنامه، حالت روشن" onClick={()=>setToast('حالت روشن در این نسخه فعال است.')}><span className="demo-settings-icon">☼</span><span><strong>ظاهر برنامه</strong><small>حالت روشن</small></span><b>‹</b></button>
          <button className="demo-settings-item" aria-label="درباره برنامه، نسخه و قوانین" onClick={()=>setToast('نسخهٔ نمایشی مونو • اطلاعات حقوقی در پنل وب.')}><span className="demo-settings-icon">i</span><span><strong>درباره برنامه</strong><small>نسخه، قوانین و حریم خصوصی</small></span><b>‹</b></button>
        </div>
        <h2 className="demo-settings-section-title demo-settings-org-title">تنظیمات سازمان</h2>
        <div className="demo-settings-org-note">مدیریت اعضا، دسترسی‌ها، اتصال‌ها و هویت بصری از پنل وب انجام می‌شود.</div>
        <small className="demo-settings-demo-label">این گزینه‌ها در محیط نمایشی به تنظیمات حساب واقعی متصل نیستند.</small>
      </>}
      {screen==='profile'&&<>
        <div className="section-head compact demo-page-heading"><h1>پروفایل</h1></div>
        <section className="demo-profile-details" aria-label="پروفایل نمایشی">
          <div className="demo-profile-identity"><span className="demo-profile-large-avatar">م</span><strong>مهدی کاظمی</strong><small>مدیر حساب</small><b>دسترسی مدیریتی</b></div>
          <h2>اطلاعات حساب</h2>
          <div className="demo-profile-field"><span>شماره همراه</span><strong dir="ltr">۰۹۱۲ ۱۲۳ ۴۵۶۷</strong></div>
          <div className="demo-profile-field"><span>ایمیل</span><strong dir="ltr">m.kazemi@example.com</strong></div>
          <div className="demo-profile-field"><span>کسب‌وکار</span><strong>فروشگاه سپهر</strong></div>
          <div className="demo-profile-field"><span>شعبه پیش‌فرض</span><strong>{activeBranch}</strong></div>
          <button className="demo-profile-edit" onClick={()=>setToast('ویرایش اطلاعات حساب در نسخهٔ نمایشی انجام نمی‌شود.')}>ویرایش مشخصات</button>
          <small className="demo-profile-demo-label">اطلاعات این صفحه نمونهٔ نمایشی هستند.</small>
        </section>
      </>}
      {screen==='finance'&&<>
        <div className="section-head compact demo-page-heading"><h1>خلاصه این ماه</h1></div>
        <section className="demo-finance-summary" aria-label="خلاصه مالی نمایشی">
          <span>هزینه ارسال‌ها</span><strong className="demo-finance-total">۳٬۸۴۰٬۰۰۰ <small>تومان</small></strong>
          <div className="demo-finance-divider"/>
          <div className="demo-finance-breakdown"><span>تسویه‌شده<strong>۳٬۲۰۰٬۰۰۰ <small>تومان</small></strong></span><span>باقی‌مانده<strong>۶۴۰٬۰۰۰ <small>تومان</small></strong></span></div>
        </section>
        <div className="section-head compact demo-finance-heading"><h2>آخرین هزینه‌ها</h2></div>
        <div className="demo-finance-list">
          {[['۸۴۳۰','۴۲٬۵۰۰'],['۸۴۲۱','۳۵٬۰۰۰'],['۸۴۱۸','۲۷٬۵۰۰'],['۸۴۱۵','۵۰٬۰۰۰']].map(([id,amount])=><article className="demo-finance-entry" key={id}><span className="demo-finance-icon">▤</span><span className="demo-finance-order"><strong>سفارش {id}</strong><small>تحویل‌شده</small></span><strong className="demo-finance-amount">{amount}<small>تومان</small></strong></article>)}
        </div>
        <p className="demo-finance-note">برای صورتحساب کامل و تطبیق مالی از پنل وب استفاده کنید.</p>
        <small className="demo-finance-demo-label">مبالغ این صفحه نمونهٔ نمایشی هستند.</small>
      </>}
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
