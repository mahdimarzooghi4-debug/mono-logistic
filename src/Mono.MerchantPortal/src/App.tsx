import { useCallback, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Branch = {
  branchId: string
  name: string
  address: string | null
  zoneId: string | null
  status: string
}
type BranchPage = { branches: Branch[] }
type DeliveryRequest = {
  delivery_request_id: string
  external_reference: string
  status: string
  created_at: string
  mission_id: string | null
  mission_status: string | null
  committed_delivery_at: string | null
}
type DeliveryPage = { items: DeliveryRequest[]; next_cursor: string | null }
type Quote = {
  quote_id: string
  version: number
  issued_at: string
  expires_at: string
  is_expired: boolean
  distance_meters: number
  fare_toman: number
  buyer_share_toman: number
  seller_share_toman: number
}
type PromiseRecord = {
  promise_id: string
  version: number
  committed_delivery_at: string
  outcome: string
}
type DeliveryDetail = DeliveryRequest & {
  quote: Quote | null
  promise: PromiseRecord | null
}
type ApiError = { error?: { code?: string; message?: string } }
type View = 'dashboard' | 'deliveries' | 'create' | 'tracking' | 'branches'

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'medium', timeStyle: 'short' })
const money = new Intl.NumberFormat('fa-IR')

function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date)
}

function statusText(value?: string | null) {
  const labels: Record<string, string> = {
    RECEIVED: 'ثبت‌شده',
    PENDING: 'در انتظار بررسی',
    QUOTED: 'دارای پیشنهاد قیمت',
    PROMISED: 'زمان تحویل تأییدشده',
    MATCHED: 'پیک تخصیص‌یافته',
    IN_PROGRESS: 'در حال ارسال',
    DELIVERED: 'تحویل‌شده',
    CANCELLED: 'لغوشده',
    ACTIVE: 'فعال',
  }
  if (!value) return 'هنوز تخصیص نیافته'
  return labels[value.toUpperCase()] || value
}

async function responseError(response: Response) {
  try {
    const body = (await response.json()) as ApiError
    return body.error?.message || body.error?.code || `خطای سرور (${response.status})`
  } catch {
    return `پاسخ نامعتبر از سرور (${response.status})`
  }
}

async function api<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${token}`,
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
    cache: 'no-store',
  })
  if (!response.ok) throw new Error(await responseError(response))
  return await response.json() as T
}

function idempotencyKey() {
  if (!globalThis.crypto?.randomUUID) throw new Error('مرورگر امن برای ساخت کلید تکرارپذیری در دسترس نیست.')
  return globalThis.crypto.randomUUID()
}

function App() {
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [connected, setConnected] = useState(false)
  const [view, setView] = useState<View>('dashboard')
  const [branches, setBranches] = useState<Branch[]>([])
  const [items, setItems] = useState<DeliveryRequest[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [detail, setDetail] = useState<DeliveryDetail | null>(null)
  const [trackingInput, setTrackingInput] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [createLoading, setCreateLoading] = useState(false)
  const [createError, setCreateError] = useState('')
  const createKey = useRef<{ fingerprint: string; key: string } | null>(null)

  const pageSummary = useMemo(() => ({
    count: items.length,
    missionCount: items.filter((item) => item.mission_id).length,
    promisedCount: items.filter((item) => item.committed_delivery_at).length,
  }), [items])

  const fetchDeliveries = useCallback(async (accessToken: string, cursor: string | null, stack: Array<string | null>) => {
    setLoading(true)
    setError('')
    try {
      const params = new URLSearchParams({ page_size: '25' })
      if (cursor) params.set('cursor', cursor)
      const page = await api<DeliveryPage>(`/v1/delivery-requests?${params}`, accessToken)
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ فهرست ارسال‌ها با API سازگار نیست.')
      setItems(page.items)
      setNextCursor(page.next_cursor || null)
      setCursorStack(stack)
      setUpdatedAt(new Date().toISOString())
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'دریافت ارسال‌ها ناموفق بود.')
      throw cause
    } finally {
      setLoading(false)
    }
  }, [])

  const connect = useCallback(async (candidate: string) => {
    const cleanToken = candidate.trim()
    if (!cleanToken) {
      setError('کلید API مشتری را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setNotice('')
    try {
      const [branchPage, deliveryPage] = await Promise.all([
        api<BranchPage>('/v1/branches', cleanToken),
        api<DeliveryPage>('/v1/delivery-requests?page_size=25', cleanToken),
      ])
      if (!Array.isArray(branchPage.branches) || !Array.isArray(deliveryPage.items)) {
        throw new Error('ساختار پاسخ API با قرارداد مورد انتظار پنل سازگار نیست.')
      }
      setToken(cleanToken)
      setTokenDraft('')
      setBranches(branchPage.branches.filter((branch) => branch.status === 'ACTIVE'))
      setItems(deliveryPage.items)
      setNextCursor(deliveryPage.next_cursor || null)
      setCursorStack([null])
      setConnected(true)
      setUpdatedAt(new Date().toISOString())
      setView('dashboard')
    } catch (cause) {
      setConnected(false)
      setToken('')
      setError(cause instanceof Error ? cause.message : 'اتصال به API انجام نشد.')
    } finally {
      setLoading(false)
    }
  }, [])

  async function refresh() {
    if (!token) return
    await fetchDeliveries(token, cursorStack.at(-1) ?? null, cursorStack)
    setNotice('اطلاعات همین صفحه به‌روز شد.')
  }

  async function openDetail(deliveryRequestId: string) {
    if (!token) return
    setLoading(true)
    setError('')
    try {
      setDetail(await api<DeliveryDetail>(`/v1/delivery-requests/${encodeURIComponent(deliveryRequestId)}`, token))
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'جزئیات ارسال دریافت نشد.')
    } finally {
      setLoading(false)
    }
  }

  async function track(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const id = trackingInput.trim()
    if (!id) return
    await openDetail(id)
  }

  async function createDelivery(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!token) {
      setCreateError('برای ثبت ارسال ابتدا به API مشتری متصل شوید.')
      return
    }
    const formElement = event.currentTarget
    const form = new FormData(formElement)
    const payload = {
      external_reference: String(form.get('external_reference') || '').trim(),
      branch_id: String(form.get('branch_id') || ''),
      pickup: {
        address_text: String(form.get('pickup_address') || '').trim(),
        contact_name: String(form.get('pickup_contact') || '').trim() || null,
        contact_phone: String(form.get('pickup_phone') || '').trim() || null,
        address_details: String(form.get('pickup_details') || '').trim() || null,
      },
      dropoff: {
        address_text: String(form.get('dropoff_address') || '').trim(),
        recipient_name: String(form.get('recipient_name') || '').trim() || null,
        recipient_phone: String(form.get('recipient_phone') || '').trim() || null,
        address_details: String(form.get('dropoff_details') || '').trim() || null,
      },
      shipment: {
        shipment_model: 'BAG_BASED',
        bag_count: Number(form.get('bag_count') || 1),
        load_class: String(form.get('load_class') || 'M'),
        fragile: form.get('fragile') === 'on',
        liquid: form.get('liquid') === 'on',
        keep_upright: form.get('keep_upright') === 'on',
      },
      requested_service: 'URBAN_DELIVERY',
      requested_priority: 'STANDARD',
      estimated_ready_at: form.get('estimated_ready_at')
        ? new Date(String(form.get('estimated_ready_at'))).toISOString()
        : null,
    }
    const fingerprint = JSON.stringify(payload)
    if (createKey.current?.fingerprint !== fingerprint) {
      try {
        createKey.current = { fingerprint, key: idempotencyKey() }
      } catch (cause) {
        setCreateError(cause instanceof Error ? cause.message : 'ثبت امن ارسال ممکن نیست.')
        return
      }
    }

    setCreateLoading(true)
    setCreateError('')
    setNotice('')
    try {
      const created = await api<{
        delivery_request_id: string
        status: string
        route_location_coordinates_status: string
      }>('/v1/delivery-requests', token, {
        method: 'POST',
        headers: { 'Idempotency-Key': createKey.current.key },
        body: JSON.stringify(payload),
      })
      createKey.current = null
      const coordinatesNote = created.route_location_coordinates_status === 'CoordinatesPending'
        ? ' برای مسیریابی، مختصات هنوز ثبت نشده و سرویس نقشه در دسترس نیست.'
        : ''
      setNotice(`ارسال با شناسهٔ ${created.delivery_request_id} ثبت شد. وضعیت: ${statusText(created.status)}.${coordinatesNote}`)
      setTrackingInput(created.delivery_request_id)
      void fetchDeliveries(token, null, [null]).catch(() => undefined)
      setView('deliveries')
      formElement.reset()
    } catch (cause) {
      setCreateError(cause instanceof Error ? cause.message : 'ثبت ارسال انجام نشد. برای تلاش مجدد از همان اطلاعات استفاده کنید.')
    } finally {
      setCreateLoading(false)
    }
  }

  function disconnect() {
    setToken('')
    setTokenDraft('')
    setConnected(false)
    setBranches([])
    setItems([])
    setNextCursor(null)
    setCursorStack([null])
    setDetail(null)
    setNotice('')
    setError('')
    setView('dashboard')
  }

  const navigation: Array<{ id: View; label: string; icon: string }> = [
    { id: 'dashboard', label: 'داشبورد', icon: '⌂' },
    { id: 'create', label: 'ارسال جدید', icon: '+' },
    { id: 'deliveries', label: 'ارسال‌ها', icon: '▤' },
    { id: 'tracking', label: 'رهگیری', icon: '⌖' },
    { id: 'branches', label: 'شعب', icon: '⌘' },
  ]

  const titles: Record<View, { title: string; subtitle: string }> = {
    dashboard: { title: 'داشبورد کسب‌وکار', subtitle: 'نمای ارسال‌ها و وضعیت ثبت‌شده در Mono' },
    deliveries: { title: 'ارسال‌های شما', subtitle: 'فهرست صفحهٔ جاری از API مشتری' },
    create: { title: 'ثبت ارسال جدید', subtitle: 'ثبت درخواست شهری با اطلاعات مرسوله و مقصد' },
    tracking: { title: 'رهگیری ارسال', subtitle: 'مشاهدهٔ وضعیت و زمان تحویل ثبت‌شده برای یک شناسه' },
    branches: { title: 'شعب کسب‌وکار', subtitle: 'شعب فعال متصل به کلید API فعلی' },
  }

  return (
    <div className="app-shell">
      <aside className="sidebar" aria-label="ناوبری اصلی">
        <div className="brand-zone">
          <div className="brand-mark" aria-hidden="true">م</div>
          <div><strong>مونو</strong><small>MONO LOGISTICS</small></div>
        </div>
        <div className="workspace-name"><span className="store-avatar">س</span><span><strong>پنل کسب‌وکار</strong><small>{connected ? `${branches.length} شعبهٔ فعال` : 'اتصال به حساب مشتری'}</small></span></div>
        <nav className="navigation">
          <p className="nav-caption">مدیریت ارسال</p>
          {navigation.map((item) => (
            <button className={`nav-item ${view === item.id ? 'active' : ''}`} key={item.id} onClick={() => { setView(item.id); setError('') }}>
              <span className="nav-icon" aria-hidden="true">{item.icon}</span>{item.label}
            </button>
          ))}
          <p className="nav-caption secondary-caption">در برنامهٔ توسعه</p>
          <button className="nav-item unavailable" disabled title="برای صورتحساب واقعی، سرویس مالی باید مشخص و متصل شود"><span className="nav-icon" aria-hidden="true">◷</span>صورتحساب <small>بعداً</small></button>
          <button className="nav-item unavailable" disabled title="مدیریت تنظیمات حساب هنوز به API متصل نشده است"><span className="nav-icon" aria-hidden="true">⚙</span>تنظیمات <small>بعداً</small></button>
        </nav>
        <div className="sidebar-footer"><span className={`status-dot ${connected ? 'on' : ''}`} />{connected ? 'متصل به API مونو' : 'هنوز متصل نشده'}</div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div className="page-title"><h1>{titles[view].title}</h1><p>{titles[view].subtitle}</p></div>
          <div className="topbar-actions">
            <span className={`system-pill ${connected ? 'online' : ''}`}><i />{connected ? 'اتصال برقرار' : 'بدون اتصال'}</span>
            {connected && <button className="icon-button" onClick={() => void refresh()} disabled={loading} aria-label="به‌روزرسانی">↻</button>}
            {connected && <button className="text-button" onClick={disconnect}>قطع اتصال</button>}
          </div>
        </header>

        {!connected && (
          <section className="connect-card">
            <div><span className="eyebrow">اتصال امن به حساب</span><h2>کلید API مشتری را وارد کنید</h2><p>کلید فقط در حافظهٔ همین صفحه می‌ماند و با بارگذاری مجدد پاک می‌شود.</p></div>
            <form className="connect-form" onSubmit={(event) => { event.preventDefault(); void connect(tokenDraft) }}>
              <label htmlFor="api-token">کلید API</label>
              <input id="api-token" type="password" autoComplete="off" value={tokenDraft} onChange={(event) => setTokenDraft(event.target.value)} placeholder="کلید مشتری با دسترسی شعب و ارسال‌ها" required />
              <button className="primary-button" disabled={loading}>{loading ? 'در حال اتصال…' : 'اتصال به پنل'}</button>
            </form>
          </section>
        )}

        {error && <div className="message error" role="alert"><strong>عملیات انجام نشد</strong><span>{error}</span><button onClick={() => setError('')} aria-label="بستن پیام">×</button></div>}
        {notice && <div className="message success" role="status"><span>{notice}</span><button onClick={() => setNotice('')} aria-label="بستن پیام">×</button></div>}

        {connected && view === 'dashboard' && (
          <>
            <section className="welcome-banner">
              <div><span className="eyebrow">{branches.length ? branches[0].name : 'حساب کسب‌وکار'}</span><h2>مدیریت ارسال‌های شهری</h2><p>ثبت درخواست، بررسی وضعیت و رهگیری ارسال‌های کسب‌وکار.</p></div>
              <button className="primary-button" onClick={() => { setView('create'); setCreateError(''); setNotice('') }}>＋ ثبت ارسال جدید</button>
            </section>
            <section className="kpi-grid" aria-label="خلاصه صفحه جاری">
              <Kpi icon="▤" label="ارسال در این صفحه" value={fa.format(pageSummary.count)} tone="cyan" />
              <Kpi icon="⌖" label="دارای مأموریت" value={fa.format(pageSummary.missionCount)} tone="blue" />
              <Kpi icon="✓" label="زمان تحویل ثبت‌شده" value={fa.format(pageSummary.promisedCount)} tone="green" />
            </section>
            <DeliveryPanel items={items.slice(0, 5)} loading={loading} onOpen={openDetail} onAll={() => setView('deliveries')} />
            <p className="data-note">خلاصه‌ها فقط از همین صفحهٔ دریافت‌شده محاسبه شده‌اند؛ عدد کل کسب‌وکار یا صورتحساب از این API به‌دست نمی‌آید. {updatedAt && `به‌روزرسانی ${formatDate(updatedAt)}`}</p>
          </>
        )}

        {connected && view === 'deliveries' && (
          <section className="panel">
            <div className="panel-heading"><div><span className="eyebrow">فهرست API</span><h2>ارسال‌ها</h2></div><span className="panel-meta">{fa.format(items.length)} مورد در این صفحه</span></div>
            <DeliveryTable items={items} loading={loading} onOpen={openDetail} />
            <div className="pagination"><span>صفحهٔ {fa.format(cursorStack.length)}</span><div><button disabled={loading || cursorStack.length <= 1} onClick={() => { const stack = cursorStack.slice(0, -1); void fetchDeliveries(token, stack.at(-1) ?? null, stack) }}>قبلی</button><button disabled={loading || !nextCursor} onClick={() => { const stack = [...cursorStack, nextCursor]; void fetchDeliveries(token, nextCursor, stack) }}>بعدی</button></div></div>
            <p className="data-note">صفحه‌بندی با نشانگر API انجام می‌شود. جست‌وجوی سراسری در قرارداد فعلی این API وجود ندارد.</p>
          </section>
        )}

        {connected && view === 'create' && (
          <section className="panel form-panel">
            <div className="panel-heading"><div><span className="eyebrow">Phase 1 · ارسال شهری</span><h2>اطلاعات ارسال</h2></div><span className="panel-meta">سرویس و اولویت طبق قرارداد فعلی ثابت هستند</span></div>
            {createError && <div className="inline-error" role="alert">{createError}</div>}
            {branches.length === 0 ? <div className="empty-state"><span>⌂</span><strong>شعبهٔ فعالی برای این کلید پیدا نشد</strong><small>برای ثبت ارسال، دسترسی branches.read و یک شعبهٔ فعال لازم است.</small></div> : (
              <form className="delivery-form" onSubmit={(event) => void createDelivery(event)}>
                <div className="form-section"><h3>سفارش و شعبه</h3><div className="form-grid two">
                  <label>شمارهٔ سفارش کسب‌وکار<input name="external_reference" required minLength={1} maxLength={128} placeholder="مثلاً ۸۴۳۰" /></label>
                  <label>شعبهٔ مبدا<select name="branch_id" required defaultValue=""><option value="" disabled>انتخاب شعبه</option>{branches.map((branch) => <option key={branch.branchId} value={branch.branchId}>{branch.name}{branch.address ? ` — ${branch.address}` : ''}</option>)}</select></label>
                </div></div>
                <div className="form-section"><h3>مبدا</h3><div className="form-grid two">
                  <label className="wide">نشانی دریافت<input name="pickup_address" required maxLength={500} placeholder="نشانی کامل مبدا" /></label>
                  <label>نام تحویل‌دهنده<input name="pickup_contact" maxLength={500} /></label><label>شماره تماس مبدا<input name="pickup_phone" inputMode="tel" maxLength={500} /></label>
                  <label className="wide">جزئیات آدرس<input name="pickup_details" maxLength={500} placeholder="واحد، طبقه، راهنمای دسترسی" /></label>
                </div></div>
                <div className="form-section"><h3>مقصد</h3><div className="form-grid two">
                  <label className="wide">نشانی گیرنده<input name="dropoff_address" required maxLength={500} placeholder="نشانی کامل مقصد" /></label>
                  <label>نام گیرنده<input name="recipient_name" maxLength={500} /></label><label>شماره تماس گیرنده<input name="recipient_phone" inputMode="tel" maxLength={500} /></label>
                  <label className="wide">جزئیات آدرس<input name="dropoff_details" maxLength={500} placeholder="واحد، طبقه، راهنمای دسترسی" /></label>
                </div></div>
                <div className="form-section"><h3>مشخصات مرسوله</h3><div className="form-grid three">
                  <label>تعداد کیسه<input name="bag_count" type="number" min={1} max={1000} defaultValue={1} required /></label>
                  <label>اندازه<select name="load_class" defaultValue="M"><option value="S">کوچک</option><option value="M">متوسط</option><option value="L">بزرگ</option></select></label>
                  <label>آمادهٔ دریافت از<input name="estimated_ready_at" type="datetime-local" /></label>
                </div><div className="check-row"><label><input name="fragile" type="checkbox" />شکستنی</label><label><input name="liquid" type="checkbox" />مایع</label><label><input name="keep_upright" type="checkbox" />این سمت بالا</label></div></div>
                <div className="form-note">ثبت درخواست، قیمت یا زمان تحویل را تضمین نمی‌کند. پیشنهاد قیمت و تعهد تحویل فقط پس از بررسی شواهد معتبر از سمت سرویس صادر می‌شوند.</div>
                <div className="form-actions"><button className="primary-button" disabled={createLoading}>{createLoading ? 'در حال ثبت…' : 'ثبت درخواست ارسال'}</button><button type="button" className="secondary-button" onClick={() => setView('deliveries')}>انصراف</button></div>
              </form>
            )}
          </section>
        )}

        {connected && view === 'tracking' && (
          <section className="panel tracking-panel">
            <div className="panel-heading"><div><span className="eyebrow">رهگیری با شناسهٔ دقیق</span><h2>وضعیت ارسال را بررسی کنید</h2></div></div>
            <form className="tracking-form" onSubmit={(event) => void track(event)}><label htmlFor="tracking-id">شناسهٔ Mono ارسال</label><div><input id="tracking-id" value={trackingInput} onChange={(event) => setTrackingInput(event.target.value)} placeholder="drq_…" required /><button className="primary-button" disabled={loading}>جست‌وجو</button></div></form>
            {detail && <DetailCard detail={detail} />}
            {!detail && <p className="data-note">برای رهگیری، شناسهٔ دریافتی هنگام ثبت ارسال را وارد کنید.</p>}
          </section>
        )}

        {connected && view === 'branches' && (
          <section className="panel"><div className="panel-heading"><div><span className="eyebrow">حساب مشتری</span><h2>شعب فعال</h2></div><span className="panel-meta">{fa.format(branches.length)} شعبه</span></div>
            {branches.length ? <div className="branch-grid">{branches.map((branch) => <article className="branch-card" key={branch.branchId}><span className="branch-symbol">⌂</span><div><strong>{branch.name}</strong><p>{branch.address || 'نشانی ثبت نشده'}</p><small>{branch.zoneId ? `زون ${branch.zoneId}` : 'زون ثبت نشده'}</small></div><span className="badge success">فعال</span></article>)}</div> : <div className="empty-state"><span>⌂</span><strong>شعبهٔ فعالی ثبت نشده است</strong><small>فهرست فقط شامل شعب فعال برگشتی از API است.</small></div>}
          </section>
        )}
      </main>

      {detail && view !== 'tracking' && <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setDetail(null) }}><section className="detail-modal" role="dialog" aria-modal="true" aria-labelledby="detail-title"><header><div><span className="eyebrow">جزئیات ارسال</span><h2 id="detail-title" dir="ltr">{detail.delivery_request_id}</h2></div><button className="icon-button" onClick={() => setDetail(null)} aria-label="بستن">×</button></header><DetailCard detail={detail} /></section></div>}
    </div>
  )
}

function Kpi({ icon, label, value, tone }: { icon: string; label: string; value: string; tone: string }) {
  return <article className="kpi-card"><span className={`kpi-icon ${tone}`} aria-hidden="true">{icon}</span><div><small>{label}</small><strong>{value}</strong></div></article>
}

function DeliveryPanel({ items, loading, onOpen, onAll }: { items: DeliveryRequest[]; loading: boolean; onOpen: (id: string) => void; onAll: () => void }) {
  return <section className="panel"><div className="panel-heading"><div><span className="eyebrow">وضعیت جاری</span><h2>آخرین ارسال‌ها</h2></div><button className="link-button" onClick={onAll}>مشاهدهٔ همه ←</button></div><DeliveryTable items={items} loading={loading} onOpen={onOpen} /></section>
}

function DeliveryTable({ items, loading, onOpen }: { items: DeliveryRequest[]; loading: boolean; onOpen: (id: string) => void }) {
  if (loading && !items.length) return <div className="empty-state"><span className="spinner" /><strong>در حال دریافت اطلاعات</strong></div>
  if (!items.length) return <div className="empty-state"><span>▤</span><strong>هنوز ارسالی برای نمایش وجود ندارد</strong><small>پس از ثبت درخواست، وضعیت اینجا نمایش داده می‌شود.</small></div>
  return <div className="table-wrap"><table><thead><tr><th>شمارهٔ سفارش</th><th>شناسهٔ ارسال</th><th>وضعیت ارسال</th><th>مأموریت</th><th>زمان تحویل</th><th>زمان ثبت</th><th>جزئیات</th></tr></thead><tbody>{items.map((item) => <tr key={item.delivery_request_id}><td>{item.external_reference}</td><td dir="ltr" className="mono">{item.delivery_request_id}</td><td><span className={`badge ${item.mission_id ? 'success' : 'neutral'}`}>{statusText(item.status)}</span></td><td>{item.mission_id ? <><span className="mono" dir="ltr">{item.mission_id}</span><small className="block">{statusText(item.mission_status)}</small></> : '—'}</td><td>{formatDate(item.committed_delivery_at)}</td><td>{formatDate(item.created_at)}</td><td><button className="history-button" onClick={() => onOpen(item.delivery_request_id)}>مشاهده</button></td></tr>)}</tbody></table></div>
}

function DetailCard({ detail }: { detail: DeliveryDetail }) {
  return <div className="detail-card"><div className="detail-status"><span className={`badge ${detail.mission_id ? 'success' : 'neutral'}`}>{statusText(detail.status)}</span><span className="detail-reference">سفارش {detail.external_reference}</span></div><dl className="detail-grid">
    <div><dt>زمان ثبت</dt><dd>{formatDate(detail.created_at)}</dd></div><div><dt>وضعیت مأموریت</dt><dd>{statusText(detail.mission_status)}</dd></div>
    <div><dt>شناسهٔ مأموریت</dt><dd dir="ltr">{detail.mission_id || 'هنوز ایجاد نشده'}</dd></div><div><dt>تعهد تحویل</dt><dd>{formatDate(detail.committed_delivery_at)}</dd></div>
  </dl>
  {detail.quote ? <section className="detail-subsection"><h3>پیشنهاد قیمت ثبت‌شده · نسخهٔ {fa.format(detail.quote.version)}</h3><div className="detail-grid"><div><dt>کرایهٔ ثبت‌شده</dt><dd>{money.format(detail.quote.fare_toman)} تومان</dd></div><div><dt>مسافت ثبت‌شده</dt><dd>{fa.format(detail.quote.distance_meters)} متر</dd></div><div><dt>اعتبار پیشنهاد</dt><dd>{detail.quote.is_expired ? 'منقضی‌شده' : `تا ${formatDate(detail.quote.expires_at)}`}</dd></div><div><dt>سهم خریدار / فروشنده</dt><dd>{money.format(detail.quote.buyer_share_toman)} / {money.format(detail.quote.seller_share_toman)} تومان</dd></div></div></section> : <div className="form-note">برای این ارسال هنوز پیشنهاد قیمت ثبت‌شده‌ای در پاسخ API موجود نیست.</div>}
  {detail.promise && <div className="promise-note"><strong>تعهد تحویل تأیید شده</strong><span>{formatDate(detail.promise.committed_delivery_at)} · نتیجه {detail.promise.outcome}</span></div>}
  </div>
}

export default App
