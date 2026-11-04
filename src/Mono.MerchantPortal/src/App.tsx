import { useCallback, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import WebhookEndpointWorkbench from './WebhookEndpointWorkbench'
import MerchantDemoPreview from './MerchantDemoPreview'
import MerchantNavIcon, { type MerchantNavId } from './MerchantNavIcon'
import MerchantBrandingSettings from './MerchantBrandingSettings'
import { PersianDateTimeField } from './PersianDateTimeField'

type Branch = {
  branchId: string
  name: string
  address: string | null
  latitude: number | null
  longitude: number | null
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
type DeliveryTimelineEvent = {
  event_id: string
  event_type: string
  aggregate_type?: string
  sequence: number
  occurred_at: string
}
type DeliveryTimeline = { delivery_request_id: string; events: DeliveryTimelineEvent[] }
type MissionTimeline = { mission_id: string; events: DeliveryTimelineEvent[] }
type ApiError = { error?: { code?: string; message?: string } }
type View = 'dashboard' | 'deliveries' | 'create' | 'tracking' | 'deliveryDetail' | 'branches' | 'billing' | 'settings' | 'branchAdd' | 'invite' | 'webhook'

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
  const [settingsTab, setSettingsTab] = useState<'integrations' | 'notifications' | 'members' | 'workspace' | 'branding'>('integrations')
  const [demoPreview, setDemoPreview] = useState(false)
  const [branches, setBranches] = useState<Branch[]>([])
  const [items, setItems] = useState<DeliveryRequest[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [detail, setDetail] = useState<DeliveryDetail | null>(null)
  const [trackingEvents, setTrackingEvents] = useState<DeliveryTimelineEvent[]>([])
  const [timelineMessage, setTimelineMessage] = useState('')
  const [trackingInput, setTrackingInput] = useState('')
  const [recentTrackingIds, setRecentTrackingIds] = useState<string[]>([])
  const [supportOpen, setSupportOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [cancelLoading, setCancelLoading] = useState(false)
  const [cancelError, setCancelError] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [createLoading, setCreateLoading] = useState(false)
  const [createError, setCreateError] = useState('')
  const createKey = useRef<{ fingerprint: string; key: string } | null>(null)
  const cancelKey = useRef<{ missionId: string; key: string } | null>(null)

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

  async function openDetail(deliveryRequestId: string, destination: 'deliveryDetail' | 'tracking' = 'deliveryDetail') {
    if (!token) return
    setLoading(true)
    setError('')
    try {
      const loaded = await api<DeliveryDetail>(`/v1/delivery-requests/${encodeURIComponent(deliveryRequestId)}`, token)
      setDetail(loaded)
      setTrackingInput(deliveryRequestId)
      setRecentTrackingIds((previous) => [loaded.delivery_request_id, ...previous.filter((id) => id !== loaded.delivery_request_id)].slice(0, 3))
      setUpdatedAt(new Date().toISOString())
      setView(destination)
      setTrackingEvents([])
      setTimelineMessage('')
      const requestTimeline = api<DeliveryTimeline>(`/v1/delivery-requests/${encodeURIComponent(deliveryRequestId)}/timeline`, token)
      const missionTimeline = loaded.mission_id
        ? api<MissionTimeline>(`/v1/missions/${encodeURIComponent(loaded.mission_id)}/timeline`, token)
        : Promise.resolve(null)
      const [requestHistory, missionHistory] = await Promise.allSettled([requestTimeline, missionTimeline])
      const events = [
        ...(requestHistory.status === 'fulfilled' ? requestHistory.value.events : []),
        ...(missionHistory.status === 'fulfilled' && missionHistory.value ? missionHistory.value.events : []),
      ]
      const uniqueEvents = Array.from(new Map(events.map((event) => [event.event_id, event])).values())
        .sort((left, right) => Date.parse(left.occurred_at) - Date.parse(right.occurred_at))
      setTrackingEvents(uniqueEvents)
      if (requestHistory.status === 'rejected' || (loaded.mission_id && missionHistory.status === 'rejected')) {
        setTimelineMessage('بخشی از سابقهٔ ارسال از API دریافت نشد؛ اطلاعات موجود نمایش داده شده است.')
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'جزئیات ارسال دریافت نشد.')
    } finally {
      setLoading(false)
    }
  }

  async function submitCancellation() {
    if (!token || !detail?.mission_id) return
    setCancelLoading(true)
    setCancelError('')
    try {
      if (cancelKey.current?.missionId !== detail.mission_id) {
        cancelKey.current = { missionId: detail.mission_id, key: idempotencyKey() }
      }
      await api<{ mission_id: string; status: string }>(`/v1/missions/${encodeURIComponent(detail.mission_id)}/cancel`, token, {
        method: 'POST',
        headers: { 'Idempotency-Key': cancelKey.current.key },
        body: JSON.stringify({ reason_code: 'CUSTOMER_CANCELLED', external_reference: detail.external_reference }),
      })
      cancelKey.current = null
      setCancelOpen(false)
      setNotice('درخواست لغو مأموریت ثبت شد و در سابقهٔ رویدادها قرار گرفت.')
      await openDetail(detail.delivery_request_id)
    } catch (cause) {
      setCancelError(cause instanceof Error ? cause.message : 'درخواست لغو ثبت نشد؛ وضعیت مأموریت را دوباره بررسی کنید.')
    } finally {
      setCancelLoading(false)
    }
  }

  async function track(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const id = trackingInput.trim()
    if (!id) return
    await openDetail(id, 'tracking')
  }

  async function createDelivery(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!token) {
      setCreateError('برای ثبت ارسال ابتدا به API مشتری متصل شوید.')
      return
    }
    const formElement = event.currentTarget
    const form = new FormData(formElement)
    const branchId = String(form.get('branch_id') || '')
    const branch = branches.find((item) => item.branchId === branchId)
    const pickupLatitude = branch?.latitude
    const pickupLongitude = branch?.longitude
    const dropoffLatitudeText = String(form.get('dropoff_latitude') || '').trim()
    const dropoffLongitudeText = String(form.get('dropoff_longitude') || '').trim()
    if (Boolean(dropoffLatitudeText) !== Boolean(dropoffLongitudeText)) {
      setCreateError('برای ثبت مختصات مقصد، طول و عرض جغرافیایی را هر دو وارد کنید.')
      return
    }
    const dropoffLatitude = dropoffLatitudeText ? Number(dropoffLatitudeText) : null
    const dropoffLongitude = dropoffLongitudeText ? Number(dropoffLongitudeText) : null
    if ((dropoffLatitude !== null && (!Number.isFinite(dropoffLatitude) || dropoffLatitude < -90 || dropoffLatitude > 90)) ||
        (dropoffLongitude !== null && (!Number.isFinite(dropoffLongitude) || dropoffLongitude < -180 || dropoffLongitude > 180))) {
      setCreateError('مختصات مقصد معتبر نیست؛ عرض باید بین ۹۰- تا ۹۰ و طول بین ۱۸۰- تا ۱۸۰ باشد.')
      return
    }
    const weightText = String(form.get('estimated_weight_kg') || '').trim()
    const payload = {
      external_reference: String(form.get('external_reference') || '').trim(),
      branch_id: branchId,
      pickup: {
        address_text: String(form.get('pickup_address') || '').trim(),
        ...(pickupLatitude !== null && pickupLatitude !== undefined && pickupLongitude !== null && pickupLongitude !== undefined
          ? { latitude: pickupLatitude, longitude: pickupLongitude }
          : {}),
        contact_name: String(form.get('pickup_contact') || '').trim() || null,
        contact_phone: String(form.get('pickup_phone') || '').trim() || null,
        address_details: String(form.get('pickup_details') || '').trim() || null,
      },
      dropoff: {
        address_text: String(form.get('dropoff_address') || '').trim(),
        ...(dropoffLatitude !== null && dropoffLongitude !== null
          ? { latitude: dropoffLatitude, longitude: dropoffLongitude }
          : {}),
        recipient_name: String(form.get('recipient_name') || '').trim() || null,
        recipient_phone: String(form.get('recipient_phone') || '').trim() || null,
        address_details: String(form.get('dropoff_details') || '').trim() || null,
      },
      shipment: {
        shipment_model: 'BAG_BASED',
        bag_count: Number(form.get('bag_count') || 1),
        estimated_weight_kg: weightText ? Number(weightText) : null,
        load_class: String(form.get('load_class') || 'M'),
        fragile: form.get('fragile') === 'on',
        liquid: form.get('liquid') === 'on',
        keep_upright: form.get('keep_upright') === 'on',
        temperature_class: form.get('chilled') === 'on' ? 'CHILLED' : null,
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

  const navigation: Array<{ id: MerchantNavId; label: string }> = [
    { id: 'dashboard', label: 'داشبورد' },
    { id: 'create', label: 'ارسال جدید' },
    { id: 'deliveries', label: 'ارسال‌ها' },
    { id: 'tracking', label: 'رهگیری' },
    { id: 'branches', label: 'شعب' },
    { id: 'billing', label: 'صورتحساب' },
    { id: 'settings', label: 'تنظیمات' },
  ]

  const titles: Record<View, { title: string; subtitle: string }> = {
    dashboard: { title: 'داشبورد کسب‌وکار', subtitle: 'نمای ارسال‌ها و وضعیت ثبت‌شده در Mono' },
    deliveries: { title: 'ارسال‌های شما', subtitle: 'فهرست صفحهٔ جاری از API مشتری' },
    create: { title: 'ارسال جدید', subtitle: 'اطلاعات ارسال را وارد کنید؛ امکان‌سنجی و قیمت فقط با دادهٔ معتبر سرویس مشخص می‌شود.' },
    tracking: { title: 'رهگیری ارسال', subtitle: 'مشاهدهٔ وضعیت و زمان تحویل ثبت‌شده برای یک شناسه' },
    deliveryDetail: { title: 'جزئیات ارسال', subtitle: detail ? `${detail.external_reference} · رهگیری وضعیت ثبت‌شده در مونو` : 'جزئیات ارسال و سابقهٔ رویدادها' },
    branches: { title: 'شعب کسب‌وکار', subtitle: 'شعب فعال متصل به کلید API فعلی' },
    billing: { title: 'صورتحساب', subtitle: 'هزینهٔ ارسال‌ها و تطبیق مالی کسب‌وکار' },
    settings: { title: 'تنظیمات Workspace', subtitle: 'مدیریت هویت بصری، اعضا، اعلان‌ها و یکپارچه‌سازی' },
    branchAdd: { title: 'افزودن شعبه', subtitle: 'ثبت مبدا جدید پس از آماده‌شدن API شعب' },
    invite: { title: 'دعوت عضو جدید', subtitle: 'مدیریت دسترسی اعضای Workspace' },
    webhook: { title: 'افزودن Webhook', subtitle: 'اتصال رویدادهای Mono به سیستم کسب‌وکار' },
  }

  if (demoPreview) return <MerchantDemoPreview onExit={() => setDemoPreview(false)} />

  return (
    <div className="app-shell">
      <aside className="sidebar" aria-label="ناوبری اصلی">
        <div className="merchant-brand-card">
          <div className="brand-mark" aria-hidden="true">م</div>
          <div><strong>پنل کسب‌وکار</strong><small>ارسال و لجستیک مونو</small><em>Powered by Mono</em></div>
        </div>
        <nav className="navigation">
          {navigation.map((item) => (
            <button className={`nav-item ${view === item.id ? 'active' : ''}`} key={item.id} onClick={() => { setView(item.id); setError('') }}>
              <span className="nav-icon"><MerchantNavIcon id={item.id} /></span>{item.label}
            </button>
          ))}
        </nav>
        <div className="sidebar-footer">
          <span className={`status-dot ${connected ? 'on' : ''}`} />
          <span>{connected ? `${fa.format(branches.length)} شعبهٔ فعال` : 'اتصال به حساب مشتری'}</span>
          {connected && <button className="sidebar-logout" onClick={disconnect}>خروج</button>}
        </div>
      </aside>

      <main className={`workspace ${connected && view === 'dashboard' ? 'dashboard-workspace' : ''} ${connected && view === 'settings' && settingsTab === 'branding' ? 'workspace-branding' : ''}`}>
        {!(connected && view === 'settings' && settingsTab === 'branding') && <header className="topbar">
          <div className="page-title"><h1>{titles[view].title}</h1><p>{titles[view].subtitle}</p></div>
          <div className={`topbar-actions ${connected && (view === 'create' || view === 'deliveryDetail') ? 'create-topbar-actions' : ''}`}>
            {connected && view === 'create' && <button className="secondary-button topbar-action" disabled title="ذخیرهٔ پیش‌نویس هنوز در API مشتری پشتیبانی نمی‌شود">ذخیره پیش‌نویس</button>}
            {connected && view === 'deliveryDetail' && <button className="secondary-button topbar-action" onClick={() => setSupportOpen(true)}>پشتیبانی</button>}
            {connected && view === 'deliveryDetail' && <button className="primary-button topbar-action" onClick={() => { setView('create'); setCreateError(''); setNotice('') }}>ارسال جدید</button>}
            {connected && view !== 'create' && view !== 'deliveries' && <button className="secondary-button topbar-action" onClick={() => { setView('tracking'); setError('') }}>رهگیری سریع</button>}
            {connected && view !== 'create' && <button className="primary-button topbar-action" onClick={() => { setView('create'); setCreateError(''); setNotice('') }}>＋ ارسال جدید</button>}
            {view !== 'create' && view !== 'deliveryDetail' && view !== 'deliveries' && <span className={`system-pill ${connected ? 'online' : ''}`}><i />{connected ? 'اتصال برقرار' : 'بدون اتصال'}</span>}
            {connected && view !== 'create' && view !== 'deliveryDetail' && view !== 'deliveries' && <button className="icon-button" onClick={() => void refresh()} disabled={loading} aria-label="به‌روزرسانی">↻</button>}
            {connected && view !== 'create' && view !== 'deliveryDetail' && view !== 'deliveries' && <button className="text-button" onClick={disconnect}>قطع اتصال</button>}
          </div>
        </header>}

        {!connected && (
          <section className="connect-card">
            <div><span className="eyebrow">اتصال امن به حساب</span><h2>کلید API مشتری را وارد کنید</h2><p>کلید فقط در حافظهٔ همین صفحه می‌ماند و با بارگذاری مجدد پاک می‌شود.</p></div>
            <form className="connect-form" onSubmit={(event) => { event.preventDefault(); void connect(tokenDraft) }}>
              <label htmlFor="api-token">کلید API</label>
              <input id="api-token" type="password" autoComplete="off" value={tokenDraft} onChange={(event) => setTokenDraft(event.target.value)} placeholder="کلید مشتری با دسترسی شعب و ارسال‌ها" required />
              <button className="primary-button" disabled={loading}>{loading ? 'در حال اتصال…' : 'اتصال به پنل'}</button>
            </form>
            <button className="demo-preview-button" type="button" onClick={() => setDemoPreview(true)}>مشاهدهٔ نسخهٔ نمایشی</button>
          </section>
        )}

        {error && <div className="message error" role="alert"><strong>عملیات انجام نشد</strong><span>{error}</span><button onClick={() => setError('')} aria-label="بستن پیام">×</button></div>}
        {notice && <div className="message success" role="status"><span>{notice}</span><button onClick={() => setNotice('')} aria-label="بستن پیام">×</button></div>}

        {connected && view === 'dashboard' && (
          <>
            <section className="dashboard-api-kpis" aria-label="شاخص‌های حساب متصل">
              <DashboardMetric label="ارسال در این صفحه" value={fa.format(pageSummary.count)} />
              <DashboardMetric label="دارای مأموریت" value={fa.format(pageSummary.missionCount)} />
              <DashboardMetric label="زمان تحویل ثبت‌شده" value={fa.format(pageSummary.promisedCount)} />
              <DashboardMetric label="شعب فعال" value={fa.format(branches.length)} />
              <DashboardMetric label="آخرین به‌روزرسانی" value={updatedAt ? new Intl.DateTimeFormat('fa-IR', { timeStyle: 'short' }).format(new Date(updatedAt)) : '—'} />
            </section>
            <div className="merchant-dashboard">
              <div className="dashboard-main-column">
                <DeliveryPanel items={items.slice(0, 6)} loading={loading} onOpen={openDetail} onAll={() => setView('deliveries')} note="این شمارش فقط از صفحهٔ فعلی API است و جمع کل سوابق را نشان نمی‌دهد." />
                <section className="panel dashboard-exceptions">
                  <div className="panel-heading"><div><span className="eyebrow">پیگیری عملیاتی</span><h2>موارد نیازمند اقدام</h2></div></div>
                  <div className="dashboard-empty-note"><strong>گزارش استثناها در API این پنل موجود نیست</strong><span>برای نمایش موارد واقعی، اتصال گزارش عملیاتی کسب‌وکار لازم است.</span></div>
                </section>
              </div>
              <aside className="dashboard-aside" aria-label="ابزارهای کسب‌وکار">
                <section className="panel dashboard-widget quick-create-widget">
                  <div className="panel-heading"><div><span className="eyebrow">ارسال جدید</span><h2>ثبت سریع ارسال</h2></div></div>
                  <p>مبدأ و مقصد را انتخاب کنید؛ هزینه و زمان پس از بررسی سرویس مشخص می‌شود.</p>
                  <div className="quick-create-fields">
                    <button onClick={() => { setView('create'); setCreateError(''); setNotice('') }}><small>مبدأ</small><span>انتخاب شعبه</span></button>
                    <button onClick={() => { setView('create'); setCreateError(''); setNotice('') }}><small>مقصد</small><span>نشانی گیرنده</span></button>
                  </div>
                  <button className="primary-button full-width" onClick={() => { setView('create'); setCreateError(''); setNotice('') }}>شروع ثبت ارسال</button>
                </section>
                <section className="panel dashboard-widget quick-track-widget">
                  <div className="panel-heading"><div><span className="eyebrow">وضعیت ارسال</span><h2>رهگیری سریع</h2></div></div>
                  <form className="quick-track-form" onSubmit={(event) => void track(event)}>
                    <label className="sr-only" htmlFor="dashboard-tracking-id">شناسهٔ ارسال یا مأموریت</label>
                    <input id="dashboard-tracking-id" value={trackingInput} onChange={(event) => setTrackingInput(event.target.value)} placeholder="شناسه سفارش یا مأموریت" required />
                    <button className="secondary-button" disabled={loading}>جست‌وجو</button>
                  </form>
                </section>
                <section className="panel dashboard-widget branch-widget">
                  <div className="panel-heading"><div><span className="eyebrow">حساب کسب‌وکار</span><h2>وضعیت شعب</h2></div><button className="link-button" onClick={() => setView('branches')}>همهٔ شعب ←</button></div>
                  {branches.length ? <ul className="branch-status-list">{branches.slice(0, 4).map((branch) => <li key={branch.branchId}><span className="branch-active">فعال</span><span>{branch.name}</span></li>)}</ul> : <p className="widget-empty">شعبهٔ فعالی برای این حساب ثبت نشده است.</p>}
                </section>
                <section className="panel dashboard-widget service-widget">
                  <div className="panel-heading"><div><span className="eyebrow">سرویس فعال</span><h2>ارسال شهری</h2></div></div>
                  <dl><div><dt>محدوده و ظرفیت</dt><dd>پس از بررسی شعبه و مرسوله</dd></div><div><dt>تعهد تحویل</dt><dd>پس از ثبت درخواست</dd></div><div><dt>شعب فعال</dt><dd>{fa.format(branches.length)}</dd></div></dl>
                  <div className="service-note"><strong>مدیریت ارسال با مونو</strong><span>تعهد تحویل و وضعیت هر ارسال از پاسخ API خوانده می‌شود.</span></div>
                </section>
              </aside>
            </div>
          </>
        )}

        {connected && view === 'deliveries' && <MerchantDeliveriesPage
          items={items} branches={branches} loading={loading} pageNumber={cursorStack.length} hasPrevious={cursorStack.length > 1} hasNext={Boolean(nextCursor)}
          onOpen={openDetail}
          onPrevious={() => { const stack = cursorStack.slice(0, -1); void fetchDeliveries(token, stack.at(-1) ?? null, stack) }}
          onNext={() => { const stack = [...cursorStack, nextCursor]; void fetchDeliveries(token, nextCursor, stack) }}
        />}

        {connected && view === 'create' && (
          <div className="create-workspace">
            <nav className="create-steps" aria-label="مراحل ثبت ارسال">
              <div className="current"><span>۱. اطلاعات ارسال</span></div>
              <div><span>۲. امکان‌سنجی و قیمت</span><small>پس از دریافت شواهد معتبر</small></div>
              <div><span>۳. تأیید و ثبت</span><small>پس از صدور قیمت و تعهد</small></div>
            </nav>
            {createError && <div className="inline-error create-error" role="alert">{createError}</div>}
            {branches.length === 0 ? <section className="panel"><div className="empty-state"><span>⌂</span><strong>شعبهٔ فعالی برای این کلید پیدا نشد</strong><small>برای ثبت ارسال، دسترسی branches.read و یک شعبهٔ فعال لازم است.</small></div></section> : (
              <form className="create-layout" onSubmit={(event) => void createDelivery(event)}>
                <div className="create-form-column">
                  <section className="panel create-form-section"><h2>مبدا و زمان آمادگی</h2><div className="create-fields two">
                    <label>شعبهٔ مبدا<select name="branch_id" required defaultValue=""><option value="" disabled>انتخاب شعبه</option>{branches.map((branch) => <option key={branch.branchId} value={branch.branchId}>{branch.name}</option>)}</select></label>
                    <label>زمان تقریبی آماده‌شدن<PersianDateTimeField name="estimated_ready_at" label="زمان تقریبی آماده‌شدن" /></label>
                    <label className="full">آدرس مبدا<input name="pickup_address" required maxLength={500} placeholder="نشانی کامل شعبه" /></label>
                  </div></section>
                  <section className="panel create-form-section"><h2>گیرنده و مقصد</h2><div className="create-fields two">
                    <label>نام گیرنده<input name="recipient_name" maxLength={500} /></label><label>شماره موبایل گیرنده<input name="recipient_phone" inputMode="tel" maxLength={500} /></label>
                    <label className="full">آدرس مقصد<input name="dropoff_address" required maxLength={500} placeholder="نشانی کامل مقصد" /></label>
                    <label>عرض جغرافیایی مقصد<input name="dropoff_latitude" inputMode="decimal" placeholder="اختیاری؛ مثال ۳۵٫۷۵۲۱" /></label>
                    <label>طول جغرافیایی مقصد<input name="dropoff_longitude" inputMode="decimal" placeholder="اختیاری؛ مثال ۵۱٫۴۰۹۸" /></label>
                    <label className="full">یادداشت تحویل<input name="dropoff_details" maxLength={500} placeholder="واحد، طبقه یا راهنمای دسترسی" /></label>
                  </div></section>
                  <section className="panel create-form-section"><h2>مشخصات بسته</h2><div className="create-fields three">
                    <label>تعداد کیسه<input name="bag_count" type="number" min={1} max={1000} defaultValue={1} required /></label>
                    <label>وزن تقریبی (کیلوگرم)<input name="estimated_weight_kg" type="number" min="0.1" step="0.1" placeholder="اختیاری" /></label>
                    <label>ردهٔ بار<select name="load_class" defaultValue="M"><option value="S">کوچک</option><option value="M">متوسط</option><option value="L">بزرگ</option></select></label>
                    <label className="full">سرویس<select name="requested_service" disabled defaultValue="URBAN_DELIVERY"><option value="URBAN_DELIVERY">ارسال شهری</option></select></label>
                  </div><div className="handling-options"><label><input name="fragile" type="checkbox" />شکستنی</label><label><input name="liquid" type="checkbox" />مایعات</label><label><input name="keep_upright" type="checkbox" />حمل عمودی</label><label><input name="chilled" type="checkbox" />نیازمند سرمایش</label></div>
                    <p className="capability-note">نوع کالا و مرجع خارجی جداگانه در API فعلی ثبت نمی‌شوند؛ اطلاعات مجاز بسته و شناسهٔ سفارش در درخواست ذخیره می‌شود.</p>
                  </section>
                  <section className="panel create-form-section"><h2>شناسه و مرجع کسب‌وکار</h2><div className="create-fields two">
                    <label>شناسه سفارش داخلی<input name="external_reference" required minLength={1} maxLength={128} placeholder="مثلاً ORD-۰۳۸۵" /></label>
                    <label>مرجع خارجی<input disabled placeholder="در API فعلی پشتیبانی نمی‌شود" /></label>
                  </div><p className="capability-note">این شناسه برای رهگیری درخواست در مونو ذخیره می‌شود.</p></section>
                </div>
                <aside className="create-result-column">
                  <section className="panel create-result-card feasibility-card"><div className="create-card-heading"><h2>نتیجه امکان‌سنجی</h2><span className="badge neutral">هنوز محاسبه نشده</span></div>
                    <SummaryLine label="فاصله مسیر" value="پس از ثبت مختصات معتبر" /><SummaryLine label="شیوه حمل پیشنهادی" value="پس از بررسی ظرفیت" /><SummaryLine label="تعهد تحویل" value="از API دریافت نشده" /><SummaryLine label="کرایه" value="از API دریافت نشده" />
                  </section>
                  <section className="panel create-result-card policy-card"><h2>کنترل سیاست‌ها</h2><ul><li><i />پوشش منطقه <small>نیازمند مختصات معتبر مبدا و مقصد</small></li><li><i />حد شعاع <small>فاصلهٔ مسیر از سرویس معتبر دریافت نشده</small></li><li><i />ظرفیت و شیوه حمل <small>قواعد اجرایی در این API نمایش داده نمی‌شود</small></li><li><i />زمان آمادگی <small>پس از ثبت و بررسی درخواست</small></li></ul></section>
                  <section className="panel create-result-card pricing-card"><h2>قیمت و تعهد</h2><SummaryLine label="کرایه محاسبه‌شده" value="هنوز صادر نشده" /><SummaryLine label="مبنای قیمت" value="قرارداد از API دریافت نشده" /><SummaryLine label="تحویل هدف" value="هنوز صادر نشده" /><p>تا وقتی قیمت و تعهد معتبر از سرویس صادر نشوند، مبلغ یا زمان تحویل نمایش داده نمی‌شود.</p></section>
                  <section className="panel create-result-card evidence-card"><h2>شواهد تصمیم</h2><p>شناسهٔ شواهد و نتیجهٔ تصمیم پس از اتصال به سرویس امکان‌سنجی در دسترس خواهد بود.</p></section>
                  <div className="create-submit-actions"><button type="button" className="secondary-button" onClick={() => setView('dashboard')}>بازگشت و ویرایش</button><button className="primary-button" disabled={createLoading}>{createLoading ? 'در حال ثبت…' : 'ثبت درخواست برای بررسی'}</button></div>
                </aside>
              </form>
            )}
          </div>
        )}

        {connected && view === 'tracking' && (
          <MerchantTrackingSearch detail={detail} events={trackingEvents} recentIds={recentTrackingIds} input={trackingInput} loading={loading} onInput={setTrackingInput} onSearch={(event) => void track(event)} onRecent={(id) => { setTrackingInput(id); void openDetail(id, 'tracking') }} onOpenDetail={() => setView('deliveryDetail')} onSupport={() => setSupportOpen(true)} />
        )}

        {connected && view === 'deliveryDetail' && detail && (
          <DeliveryTrackingPage
            detail={detail}
            events={trackingEvents}
            timelineMessage={timelineMessage}
            checkedAt={updatedAt}
            onCancel={() => { setCancelError(''); setCancelOpen(true) }}
            onSupport={() => setSupportOpen(true)}
            onMissionDetails={() => document.getElementById('delivery-timeline')?.scrollIntoView({ behavior: 'smooth', block: 'start' })}
          />
        )}

        {connected && view === 'branches' && (
          <section className="panel"><div className="panel-heading"><div><span className="eyebrow">حساب مشتری</span><h2>شعب فعال</h2></div><div className="panel-heading-actions"><span className="panel-meta">{fa.format(branches.length)} شعبه</span><button className="primary-button" onClick={() => setView('branchAdd')}>＋ افزودن شعبه</button></div></div>
            {branches.length ? <div className="branch-grid">{branches.map((branch) => <article className="branch-card" key={branch.branchId}><span className="branch-symbol">⌂</span><div><strong>{branch.name}</strong><p>{branch.address || 'نشانی ثبت نشده'}</p><small>{branch.zoneId ? `زون ${branch.zoneId}` : 'زون ثبت نشده'}</small></div><span className="badge success">فعال</span></article>)}</div> : <div className="empty-state"><span>⌂</span><strong>شعبهٔ فعالی ثبت نشده است</strong><small>فهرست فقط شامل شعب فعال برگشتی از API است.</small></div>}
          </section>
        )}

        {connected && view === 'billing' && (
          <section className="panel">
            <div className="panel-heading"><div><span className="eyebrow">صورتحساب</span><h2>تطبیق مالی ارسال‌ها</h2></div><span className="panel-meta">سرویس مالی متصل نیست</span></div>
            <div className="settings-summary-grid">
              <article><small>صورتحساب باز</small><strong>—</strong></article><article><small>مبلغ قابل پرداخت</small><strong>—</strong></article><article><small>ارسال‌های دوره</small><strong>—</strong></article><article><small>پرداخت‌شده این ماه</small><strong>—</strong></article>
            </div>
            <div className="empty-state"><span>◷</span><strong>اطلاعات مالی از API صورتحساب در دسترس نیست</strong><small>مقادیر نمایشی فیگما عمداً به‌عنوان اطلاعات واقعی نمایش داده نمی‌شوند. برای تکمیل این صفحه، سرویس صورتحساب و خروجی CSV باید به API متصل شود.</small></div>
          </section>
        )}

        {connected && view === 'settings' && settingsTab === 'branding' && <MerchantBrandingSettings mode="connected" onTabChange={setSettingsTab} />}

        {connected && view === 'settings' && (
          <section className="panel settings-panel">
            <div className="panel-heading"><div><span className="eyebrow">Workspace برند سپهر</span><h2>تنظیمات Workspace</h2></div><span className="panel-meta">دسترسی API محدود</span></div>
            <p className="settings-intro">مدیریت هویت بصری، اعضا، اعلان‌ها و اتصال‌ها بر اساس امکاناتی که واقعاً برای حساب فعال شده‌اند.</p>
            <div className="settings-tabs" role="tablist" aria-label="بخش‌های تنظیمات">
              {([
                ['branding', 'هویت بصری'], ['workspace', 'Workspace'], ['members', 'اعضا و دسترسی'], ['notifications', 'اعلان‌ها'], ['integrations', 'یکپارچه‌سازی'],
              ] as const).map(([id, label]) => <button key={id} role="tab" aria-selected={settingsTab === id} className={settingsTab === id ? 'selected' : ''} onClick={() => setSettingsTab(id)}>{label}</button>)}
            </div>
            {settingsTab === 'integrations' && <div className="settings-content"><div className="panel-heading"><div><span className="eyebrow">API و Webhook</span><h3>اتصال سیستم‌های کسب‌وکار به مونو</h3></div><span className="badge success">API مشتری متصل</span></div><div className="settings-summary-grid"><article><small>API</small><strong>فعال</strong></article><article><small>مدیریت Endpoint</small><strong>در دسترس با مجوز</strong></article><article><small>سلامت تحویل ۲۴ ساعت</small><strong>از این پنل گزارش نمی‌شود</strong></article></div><p className="data-note">ثبت Endpoint، انتخاب رویداد، غیرفعال‌سازی و چرخش secret به API مشتری متصل‌اند و به scope ‏webhooks.manage نیاز دارند. سلامت تحویل ۲۴ ساعته از API این پنل گزارش نمی‌شود.</p><div className="form-actions"><button className="secondary-button" onClick={() => setView('webhook')}>＋ مدیریت Webhook Endpoint</button><button className="secondary-button" disabled aria-label="مستندات API">مستندات API</button></div></div>}
            {settingsTab === 'notifications' && <div className="settings-content"><div className="panel-heading"><div><span className="eyebrow">کانال‌ها و رویدادها</span><h3>تنظیمات اعلان‌ها</h3></div></div><div className="empty-state"><span>♧</span><strong>تنظیمات اعلان حساب در API موجود نیست</strong><small>کانال داخل پنل، پیامک و ایمیل پس از افزودن تنظیمات پایدار حساب قابل‌مدیریت می‌شوند.</small></div></div>}
            {settingsTab === 'members' && <div className="settings-content"><div className="panel-heading"><div><span className="eyebrow">نقش‌ها و محدوده دسترسی</span><h3>اعضای Workspace</h3></div><button className="secondary-button" onClick={() => setView('invite')}>دعوت عضو جدید</button></div><div className="empty-state"><span>♙</span><strong>فهرست اعضا از API حساب دریافت نمی‌شود</strong><small>تا اضافه‌شدن API اعضا و نقش‌ها، نام و تعداد نمونهٔ فیگما نمایش داده نمی‌شود.</small></div></div>}
            {settingsTab === 'workspace' && <div className="settings-content"><div className="panel-heading"><div><span className="eyebrow">مشخصات Workspace</span><h3>پیکربندی فضای کاری</h3></div></div><div className="settings-summary-grid"><article><small>نام Workspace</small><strong>از API مشتری</strong></article><article><small>شناسه Workspace</small><strong>از API مشتری</strong></article><article><small>کشور</small><strong>ایران</strong></article></div><p className="data-note">نام و شناسه در نسخهٔ جاری پاسخ API مشتری ارائه نمی‌شود؛ برای جلوگیری از نمایش دادهٔ ساختگی، فیلدها قابل تغییر نیستند.</p></div>}
          </section>
        )}

        {connected && view === 'branchAdd' && (
          <section className="panel settings-content"><div className="panel-heading"><div><span className="eyebrow">شعب</span><h2>افزودن شعبه</h2></div></div><div className="empty-state"><span>⌂</span><strong>ثبت شعبه هنوز از پنل مشتری پشتیبانی نمی‌شود</strong><small>برای فعال‌کردن این فرم باید API ایجاد و ویرایش شعبه در دسترس باشد.</small></div><div className="settings-form-grid"><label>نام شعبه<input disabled placeholder="مثلاً شعبه ونک" /></label><label>منطقه<input disabled placeholder="منطقهٔ خدمت" /></label><label className="wide">نشانی<input disabled placeholder="نشانی کامل مبدا" /></label></div><div className="form-actions"><button className="secondary-button" onClick={() => setView('branches')}>بازگشت به شعب</button><button className="primary-button" disabled>ثبت شعبه</button></div></section>
        )}

        {connected && view === 'invite' && (
          <section className="panel settings-content"><div className="panel-heading"><div><span className="eyebrow">اعضا و دسترسی</span><h2>دعوت عضو جدید</h2></div></div><div className="empty-state"><span>♙</span><strong>دعوت اعضا هنوز به API نقش‌ها متصل نیست</strong><small>هیچ دعوتی ثبت نمی‌شود تا endpoint دعوت و ممیزی دسترسی آماده شود.</small></div><div className="settings-form-grid"><label>نام عضو<input disabled placeholder="نام و نام خانوادگی" /></label><label>ایمیل یا شماره همراه<input disabled placeholder="راه تماس عضو" /></label><label>نقش Workspace<select disabled><option>انتخاب نقش</option></select></label><label>محدوده شعب<select disabled><option>انتخاب شعب</option></select></label></div><div className="form-actions"><button className="secondary-button" onClick={() => { setView('settings'); setSettingsTab('members') }}>بازگشت</button><button className="primary-button" disabled>ارسال دعوت</button></div></section>
        )}

        {connected && view === 'webhook' && (
          <WebhookEndpointWorkbench token={token} onBack={() => { setView('settings'); setSettingsTab('integrations') }} />
        )}

      </main>

      {detail && view !== 'tracking' && view !== 'deliveryDetail' && <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setDetail(null) }}><section className="detail-modal" role="dialog" aria-modal="true" aria-labelledby="detail-title"><header><div><span className="eyebrow">جزئیات ارسال</span><h2 id="detail-title" dir="ltr">{detail.delivery_request_id}</h2></div><button className="icon-button" onClick={() => setDetail(null)} aria-label="بستن">×</button></header><DetailCard detail={detail} /></section></div>}
      {supportOpen && detail && <SupportDialog detail={detail} onClose={() => setSupportOpen(false)} />}
      {cancelOpen && detail && <CancelDialog detail={detail} loading={cancelLoading} error={cancelError} onClose={() => setCancelOpen(false)} onConfirm={() => void submitCancellation()} />}
    </div>
  )
}

function DashboardMetric({ label, value }: { label: string; value: string }) {
  return <article className="dashboard-metric"><small>{label}</small><strong>{value}</strong></article>
}

function SummaryLine({ label, value }: { label: string; value: string }) {
  return <div className="create-summary-line"><span>{label}</span><strong>{value}</strong></div>
}

function DeliveryPanel({ items, loading, onOpen, onAll, note }: { items: DeliveryRequest[]; loading: boolean; onOpen: (id: string) => void; onAll: () => void; note?: string }) {
  return <section className="panel dashboard-deliveries-panel"><div className="panel-heading"><div><span className="eyebrow">وضعیت جاری</span><h2>آخرین ارسال‌ها</h2></div><button className="link-button" onClick={onAll}>مشاهدهٔ همه ←</button></div><DeliveryTable items={items} loading={loading} onOpen={onOpen} />{note && <p className="dashboard-data-note">{note}</p>}</section>
}

function DeliveryTable({ items, loading, onOpen }: { items: DeliveryRequest[]; loading: boolean; onOpen: (id: string) => void }) {
  if (loading && !items.length) return <div className="empty-state"><span className="spinner" /><strong>در حال دریافت اطلاعات</strong></div>
  if (!items.length) return <div className="empty-state"><span>▤</span><strong>هنوز ارسالی برای نمایش وجود ندارد</strong><small>پس از ثبت درخواست، وضعیت اینجا نمایش داده می‌شود.</small></div>
  return <div className="table-wrap"><table><thead><tr><th>شمارهٔ سفارش</th><th>شناسهٔ ارسال</th><th>وضعیت ارسال</th><th>مأموریت</th><th>زمان تحویل</th><th>زمان ثبت</th><th>جزئیات</th></tr></thead><tbody>{items.map((item) => <tr key={item.delivery_request_id}><td>{item.external_reference}</td><td dir="ltr" className="mono">{item.delivery_request_id}</td><td><span className={`badge ${item.mission_id ? 'success' : 'neutral'}`}>{statusText(item.status)}</span></td><td>{item.mission_id ? <><span className="mono" dir="ltr">{item.mission_id}</span><small className="block">{statusText(item.mission_status)}</small></> : '—'}</td><td>{formatDate(item.committed_delivery_at)}</td><td>{formatDate(item.created_at)}</td><td><button className="history-button" onClick={() => onOpen(item.delivery_request_id)}>مشاهده</button></td></tr>)}</tbody></table></div>
}

type DeliveryFilters = { query: string; status: string; from: string; to: string }
const emptyDeliveryFilters: DeliveryFilters = { query: '', status: '', from: '', to: '' }

function MerchantDeliveriesPage({ items, branches, loading, pageNumber, hasPrevious, hasNext, onOpen, onPrevious, onNext }: {
  items: DeliveryRequest[]; branches: Branch[]; loading: boolean; pageNumber: number; hasPrevious: boolean; hasNext: boolean
  onOpen: (id: string) => void; onPrevious: () => void; onNext: () => void
}) {
  const [draft, setDraft] = useState<DeliveryFilters>(emptyDeliveryFilters)
  const [filters, setFilters] = useState<DeliveryFilters>(emptyDeliveryFilters)
  const availableStatuses = Array.from(new Set(items.map((item) => item.status).filter(Boolean)))
  const filtered = items.filter((item) => {
    const query = filters.query.trim().toLocaleLowerCase()
    const matchesQuery = !query || [item.external_reference, item.delivery_request_id, item.mission_id || ''].some((value) => value.toLocaleLowerCase().includes(query))
    const matchesStatus = !filters.status || item.status === filters.status
    const createdDate = item.created_at.slice(0, 10)
    const matchesFrom = !filters.from || createdDate >= filters.from
    const matchesTo = !filters.to || createdDate <= filters.to
    return matchesQuery && matchesStatus && matchesFrom && matchesTo
  })
  const deliveredCount = items.filter((item) => (item.mission_status || item.status).toUpperCase() === 'DELIVERED').length
  const missionCount = items.filter((item) => item.mission_id).length
  const promisedCount = items.filter((item) => item.committed_delivery_at).length

  function exportCsv() {
    const headers = ['شماره سفارش', 'شناسه ارسال', 'وضعیت ارسال', 'شناسه مأموریت', 'وضعیت مأموریت', 'زمان تحویل تعهدشده', 'زمان ثبت']
    const rows = filtered.map((item) => [item.external_reference, item.delivery_request_id, statusText(item.status), item.mission_id || '', statusText(item.mission_status), item.committed_delivery_at || '', item.created_at])
    const csv = [headers, ...rows].map((row) => row.map((value) => `"${String(value).replaceAll('"', '""')}"`).join(',')).join('\r\n')
    const url = URL.createObjectURL(new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8' }))
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `mono-deliveries-page-${pageNumber}.csv`
    anchor.click()
    URL.revokeObjectURL(url)
  }

  function resetFilters() {
    setDraft(emptyDeliveryFilters)
    setFilters(emptyDeliveryFilters)
  }

  return <section className="merchant-deliveries-page" dir="rtl">
    <div className="delivery-list-toolbar"><span className="panel-meta">خروجی فقط برای داده‌های فیلترشدهٔ صفحهٔ جاری ساخته می‌شود.</span><button className="secondary-button" onClick={exportCsv}>خروجی CSV</button></div>
    <section className="delivery-list-metrics" aria-label="خلاصهٔ صفحهٔ جاری">
      <DetailMetric label="ارسال در این صفحه" value={fa.format(items.length)} />
      <DetailMetric label="دارای مأموریت" value={fa.format(missionCount)} />
      <DetailMetric label="تعهد تحویل ثبت‌شده" value={fa.format(promisedCount)} />
      <DetailMetric label="تحویل‌شده در این صفحه" value={fa.format(deliveredCount)} />
      <DetailMetric label="شعب فعال حساب" value={fa.format(branches.length)} />
    </section>
    <section className="panel delivery-list-filter-panel"><div className="delivery-list-filters">
      <label className="delivery-search-field">جست‌وجو در صفحهٔ جاری<input value={draft.query} onChange={(event) => setDraft((current) => ({ ...current, query: event.target.value }))} placeholder="شماره سفارش، شناسه ارسال یا مأموریت" /></label>
      <label>وضعیت ارسال<select value={draft.status} onChange={(event) => setDraft((current) => ({ ...current, status: event.target.value }))}><option value="">همهٔ وضعیت‌ها</option>{availableStatuses.map((status) => <option key={status} value={status}>{statusText(status)}</option>)}</select></label>
      <label>شعبه<select disabled aria-label="فیلتر شعبه در دسترس نیست"><option>همهٔ شعب · اتصال در API موجود نیست</option></select></label>
      <label>از تاریخ<input type="date" value={draft.from} onChange={(event) => setDraft((current) => ({ ...current, from: event.target.value }))} /></label>
      <label>تا تاریخ<input type="date" value={draft.to} onChange={(event) => setDraft((current) => ({ ...current, to: event.target.value }))} /></label>
      <button className="primary-button apply-delivery-filters" onClick={() => setFilters(draft)}>اعمال فیلتر</button>
    </div></section>
    <div className="delivery-list-columns">
      <section className="panel delivery-list-table-panel"><div className="panel-heading"><div><span className="eyebrow">ارسال‌های مشتری</span><h2>نتایج ارسال</h2></div><span className="panel-meta">{fa.format(filtered.length)} از {fa.format(items.length)} مورد این صفحه</span></div>
        <div className="merchant-delivery-table-wrap"><table className="merchant-delivery-table"><thead><tr><th>وضعیت</th><th>زمان تحویل</th><th>کرایه</th><th>شعبه</th><th>گیرنده</th><th>پیک / مأموریت</th><th>شماره سفارش</th><th>شناسه ارسال</th><th>جزئیات</th></tr></thead><tbody>
          {filtered.map((item) => <tr key={item.delivery_request_id}><td><span className={`badge ${item.mission_id ? 'success' : 'neutral'}`}>{statusText(item.mission_status || item.status)}</span></td><td>{formatDate(item.committed_delivery_at)}</td><td>از API دریافت نمی‌شود</td><td>در API فهرست نیست</td><td>در API فهرست نیست</td><td>{item.mission_id ? <><span dir="ltr" className="mono">{item.mission_id}</span><small className="block">{statusText(item.mission_status)}</small></> : 'مأموریت ثبت نشده'}</td><td>{item.external_reference || '—'}</td><td dir="ltr" className="mono">{item.delivery_request_id}</td><td><button className="history-button" onClick={() => onOpen(item.delivery_request_id)}>جزئیات</button></td></tr>)}
        </tbody></table>{loading && <div className="table-loading" role="status">در حال دریافت فهرست…</div>}{!loading && !filtered.length && <div className="empty-state"><span>⌕</span><strong>{items.length ? 'با این فیلتر موردی پیدا نشد' : 'ارسالی در این صفحه نیست'}</strong><small>{items.length ? 'فیلترها را تغییر دهید یا پاک کنید.' : 'پس از ثبت ارسال، موارد واقعی از API اینجا نمایش داده می‌شوند.'}</small></div>}</div>
        <div className="pagination delivery-list-pagination"><span>صفحهٔ {fa.format(pageNumber)} · {fa.format(filtered.length)} نتیجه در صفحه</span><div><button disabled={loading || !hasPrevious} onClick={onPrevious}>قبلی</button><button disabled={loading || !hasNext} onClick={onNext}>بعدی</button></div></div>
      </section>
      <aside className="delivery-list-side">
        <section className="panel delivery-list-summary"><h2>خلاصهٔ فیلتر</h2><SummaryLine label="وضعیت" value={filters.status ? statusText(filters.status) : 'همهٔ وضعیت‌ها'} /><SummaryLine label="بازهٔ تاریخ" value={filters.from || filters.to ? `${filters.from || 'ابتدا'} تا ${filters.to || 'امروز'}` : 'همهٔ تاریخ‌ها'} /><SummaryLine label="نتیجهٔ این صفحه" value={fa.format(filtered.length)} /><p>جست‌وجو و فیلتر تاریخ فقط روی صفحهٔ دریافت‌شده اعمال می‌شوند؛ API فهرست، فیلتر سراسری و شمارش کل ندارد.</p><button className="secondary-button" onClick={resetFilters}>پاک‌کردن فیلترها</button></section>
        <section className="panel delivery-list-exceptions"><div className="panel-heading"><div><h2>موارد نیازمند اقدام</h2></div><span className="badge neutral">داده‌ای نیست</span></div><p>گزارش استثناها و علت نیاز به اقدام در API مشتری ارائه نمی‌شود.</p></section>
        <section className="panel delivery-list-saved-view"><h2>نمای ذخیره‌شده</h2><p>ذخیره و اشتراک نمای فیلترشده در API حساب پشتیبانی نمی‌شود.</p><button className="secondary-button" disabled>ذخیرهٔ نمای فعلی</button></section>
      </aside>
    </div>
  </section>
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

const eventLabels: Record<string, string> = {
  'quote.issued': 'پیشنهاد قیمت ثبت شد', 'promise.committed': 'تعهد تحویل ثبت شد', 'promise.revised': 'تعهد تحویل به‌روزرسانی شد',
  'mission.created': 'مأموریت ایجاد شد', 'mission.promised': 'زمان تحویل تأیید شد', 'mission.searching_courier': 'جست‌وجوی پیک آغاز شد',
  'courier.assigned': 'پیک تخصیص یافت', 'mission.courier_assigned': 'پیک تخصیص یافت', 'courier.arrived_pickup': 'پیک به مبدأ رسید',
  'mission.arrived_pickup': 'پیک به مبدأ رسید', 'mission.handed_to_courier': 'مرسوله به پیک تحویل شد',
  'mission.picked_up': 'مرسوله دریافت شد', 'mission.in_transit': 'ارسال در مسیر است', 'mission.arrived_dropoff': 'پیک به مقصد رسید',
  'mission.delivered': 'مرسوله تحویل شد', 'mission.failed': 'ارسال ناموفق بود', 'mission.cancelled': 'مأموریت لغو شد',
}

function DeliveryTrackingPage({ detail, events, timelineMessage, checkedAt, onCancel, onSupport, onMissionDetails }: {
  detail: DeliveryDetail; events: DeliveryTimelineEvent[]; timelineMessage: string; checkedAt: string | null
  onCancel: () => void; onSupport: () => void; onMissionDetails: () => void
}) {
  const canCancel = Boolean(detail.mission_id && ['PROMISED', 'SEARCHING_COURIER', 'COURIER_ASSIGNED', 'ARRIVED_PICKUP'].includes((detail.mission_status || '').toUpperCase()))
  const latestEvent = events.at(-1)
  return <main className="delivery-detail-page" dir="rtl">
    <div className="delivery-detail-banner"><span className="detail-banner-icon">✓</span><div><strong>وضعیت ارسال از سامانه دریافت شد</strong><span>اطلاعات این صفحه بر اساس پاسخ API و رویدادهای ثبت‌شده است.</span></div><span className="badge success">{statusText(detail.mission_status || detail.status)}</span></div>
    <section className="delivery-detail-metrics" aria-label="خلاصه ارسال">
      <DetailMetric label="وضعیت فعلی" value={statusText(detail.mission_status || detail.status)} />
      <DetailMetric label="تعهد تحویل" value={formatDate(detail.committed_delivery_at)} />
      <DetailMetric label="زمان ثبت" value={formatDate(detail.created_at)} />
      <DetailMetric label="شناسه مأموریت" value={detail.mission_id || 'ایجاد نشده'} />
      <DetailMetric label="آخرین رویداد ثبت‌شده" value={latestEvent ? formatDate(latestEvent.occurred_at) : 'رویدادی موجود نیست'} />
    </section>
    <div className="delivery-detail-columns">
      <div className="delivery-detail-main">
        <section className="panel delivery-map-panel"><div className="panel-heading"><div><span className="eyebrow">نمای مسیر</span><h2>موقعیت و مسیر ارسال</h2></div><span className="panel-meta">موقعیت زنده در دسترس نیست</span></div>
          <div className="delivery-map-placeholder"><span className="map-pin">⌖</span><strong>مختصات مسیر در API مشتری موجود نیست</strong><p>برای نمایش مسیر یا موقعیت زندهٔ پیک، API مکان‌یابی مجاز لازم است. وضعیت زمانی رویدادها در سابقهٔ ارسال نمایش داده می‌شود.</p></div>
        </section>
        <section className="panel delivery-timeline-panel" id="delivery-timeline"><div className="panel-heading"><div><span className="eyebrow">سابقهٔ واقعی</span><h2>رویدادهای ارسال</h2></div><span className="panel-meta">{fa.format(events.length)} رویداد</span></div>
          {timelineMessage && <p className="data-note" role="status">{timelineMessage}</p>}
          {events.length ? <ol className="delivery-event-list">{events.map((event) => <li key={event.event_id}><span className="event-marker" /><div><strong>{eventLabels[event.event_type] || event.event_type}</strong><small>{formatDate(event.occurred_at)}</small></div><code dir="ltr">{event.event_id}</code></li>)}</ol> : <div className="empty-state"><span>◷</span><strong>رویدادی برای این ارسال برنگشت</strong><small>در صورت ایجاد مأموریت، رویدادهای ثبت‌شدهٔ آن در این بخش می‌آیند.</small></div>}
        </section>
      </div>
      <aside className="delivery-detail-side">
        <section className="panel delivery-summary-panel"><div className="panel-heading"><div><span className="eyebrow">خلاصه</span><h2>اطلاعات ارسال</h2></div></div>
          <SummaryLine label="شماره سفارش" value={detail.external_reference || '—'} /><SummaryLine label="شناسه ارسال" value={detail.delivery_request_id} /><SummaryLine label="شناسه مأموریت" value={detail.mission_id || 'هنوز ایجاد نشده'} /><SummaryLine label="کرایه ثبت‌شده" value={detail.quote ? `${money.format(detail.quote.fare_toman)} تومان` : 'در API موجود نیست'} /><SummaryLine label="زمان تحویل" value={formatDate(detail.committed_delivery_at)} />
        </section>
        <section className="panel delivery-recipient-panel"><div className="panel-heading"><div><span className="eyebrow">مرسوله</span><h2>گیرنده و بسته</h2></div></div><p>نام و تماس گیرنده، نشانی مقصد و مشخصات بسته در پاسخ API جزئیات ارسال ارائه نمی‌شوند.</p></section>
        <section className="panel delivery-actions-panel"><div className="panel-heading"><div><span className="eyebrow">اقدام‌ها</span><h2>کمک و پیگیری</h2></div></div>
          <p>آخرین بررسی این صفحه: {formatDate(checkedAt)}{latestEvent ? ` · شناسه رویداد: ${latestEvent.event_id}` : ''}</p>
          <button className="secondary-button" disabled={!canCancel} onClick={onCancel}>درخواست لغو مأموریت</button>
          {!canCancel && <small className="capability-note">لغو فقط پیش از تحویل مرسوله به پیک و در وضعیت مجاز امکان‌پذیر است.</small>}
          <button className="secondary-button" onClick={onSupport}>اطلاعات تماس پشتیبانی</button>
          <button className="link-button" onClick={onMissionDetails}>مشاهدهٔ سابقهٔ کامل مأموریت ←</button>
        </section>
      </aside>
    </div>
  </main>
}

function DetailMetric({ label, value }: { label: string; value: string }) {
  return <article><small>{label}</small><strong>{value}</strong></article>
}

function MerchantTrackingSearch({ detail, events, recentIds, input, loading, onInput, onSearch, onRecent, onOpenDetail, onSupport }: {
  detail: DeliveryDetail | null; events: DeliveryTimelineEvent[]; recentIds: string[]; input: string; loading: boolean
  onInput: (value: string) => void; onSearch: (event: FormEvent<HTMLFormElement>) => void; onRecent: (id: string) => void
  onOpenDetail: () => void; onSupport: () => void
}) {
  const latest = events.at(-1)
  const selected = Boolean(detail)
  return <main className="merchant-tracking-page" dir="rtl">
    <section className="tracking-search-card"><h2>چه چیزی را می‌خواهی رهگیری کنی؟</h2><form className="tracking-search-row" onSubmit={onSearch}>
      <label className="sr-only" htmlFor="tracking-id">شناسهٔ ارسال یا مأموریت</label><input id="tracking-id" value={input} onChange={(event) => onInput(event.target.value)} placeholder="شناسهٔ ارسال Mono یا مأموریت" required />
      <button className="primary-button" disabled={loading}>{loading ? 'در حال جست‌وجو…' : 'جست‌وجو'}</button>
    </form></section>
    {selected && detail ? <>
      <section className="tracking-result-metrics" aria-label="وضعیت ثبت‌شده">
        <DetailMetric label="وضعیت" value={statusText(detail.mission_status || detail.status)} />
        <DetailMetric label="تعهد تحویل" value={formatDate(detail.committed_delivery_at)} />
        <DetailMetric label="ETA زنده" value="در API موجود نیست" />
        <DetailMetric label="پیک" value="در API موجود نیست" />
        <DetailMetric label="به‌روزرسانی وضعیت" value={latest ? formatDate(latest.occurred_at) : formatDate(detail.created_at)} />
      </section>
      <div className="tracking-result-columns">
        <section className="tracking-live-card">
          <div className="tracking-card-heading"><div><strong>{detail.external_reference || detail.delivery_request_id}{detail.mission_id ? ` · ${detail.mission_id}` : ''}</strong><small>اطلاعات فقط از API مشتری</small></div><span className="badge neutral">موقعیت زنده موجود نیست</span></div>
          <div className="tracking-map-unavailable"><span aria-hidden="true">⌖</span><strong>موقعیت و مسیر از API در دسترس نیست</strong><p>این صفحه فقط وضعیت‌ها و زمان رویدادهای ثبت‌شده را نشان می‌دهد. برای نمایش نقشهٔ زنده، API مجاز مکان‌یابی لازم است.</p></div>
        </section>
        <aside className="tracking-side-column">
          <section className="tracking-summary-card"><h2>خلاصهٔ ارسال</h2>
            <TrackingSummaryLine label="سفارش" value={detail.external_reference || '—'} /><TrackingSummaryLine label="شناسهٔ ارسال" value={detail.delivery_request_id} /><TrackingSummaryLine label="مأموریت" value={detail.mission_id || 'ایجاد نشده'} /><TrackingSummaryLine label="گیرنده و شعبه" value="در API موجود نیست" /><TrackingSummaryLine label="کرایه" value={detail.quote ? `${money.format(detail.quote.fare_toman)} تومان` : 'در API موجود نیست'} /><TrackingSummaryLine label="آخرین رویداد" value={latest ? formatDate(latest.occurred_at) : 'رویدادی ثبت نشده'} />
          </section>
          <section className="tracking-actions-card"><h2>اقدام</h2><button className="primary-button" onClick={onOpenDetail}>باز کردن جزئیات ارسال</button><button className="secondary-button" onClick={onSupport}>اطلاعات پیگیری پشتیبانی</button><small>لغو در جزئیات ارسال و فقط در وضعیت مجاز انجام می‌شود.</small></section>
        </aside>
      </div>
      <div className="tracking-lower-columns"><section className="tracking-timeline-card"><div className="tracking-card-heading"><strong>آخرین رویدادها</strong><span className="badge neutral">{fa.format(events.length)} رویداد</span></div>
        {events.length ? <ol className="tracking-event-list">{events.slice(-4).reverse().map((event) => <li key={event.event_id}><time>{formatDate(event.occurred_at)}</time><div><strong>{eventLabels[event.event_type] || event.event_type}</strong><small>{event.event_type}</small></div></li>)}</ol> : <p className="data-note">برای این ارسال هنوز رویدادی از API برنگشته است.</p>}
      </section><section className="tracking-recent-card"><h2>جست‌وجوهای اخیر این نشست</h2>{recentIds.length ? recentIds.map((id) => <button key={id} onClick={() => onRecent(id)}><span className="badge neutral">بازکردن</span><b dir="ltr">{id}</b></button>) : <p>پس از جست‌وجوی موفق، شناسه‌ها تا پایان این نشست اینجا می‌مانند.</p>}</section></div>
    </> : <section className="tracking-empty-card"><strong>{loading ? 'در حال دریافت اطلاعات ارسال…' : 'ارسال را با شناسهٔ دقیق پیدا کنید'}</strong><p>جست‌وجو فقط با شناسهٔ ارسال یا مأموریت پشتیبانی می‌شود. نام گیرنده و شماره موبایل در API مشتری برای جست‌وجو موجود نیست.</p></section>}
  </main>
}

function TrackingSummaryLine({ label, value }: { label: string; value: string }) {
  return <div className="tracking-summary-line"><span>{label}</span><strong>{value}</strong></div>
}

function SupportDialog({ detail, onClose }: { detail: DeliveryDetail; onClose: () => void }) {
  const [copied, setCopied] = useState(false)
  async function copyId() {
    try { await navigator.clipboard.writeText(detail.delivery_request_id); setCopied(true) } catch { setCopied(false) }
  }
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}><section className="action-dialog" role="dialog" aria-modal="true" aria-labelledby="support-title"><header><div><span className="eyebrow">پشتیبانی Mono</span><h2 id="support-title">اطلاعات پیگیری</h2></div><button className="icon-button" onClick={onClose} aria-label="بستن">×</button></header><p>در حال حاضر API ارسال، ثبت تیکت یا شمارهٔ تماس پشتیبانی را ارائه نمی‌کند. شناسهٔ زیر را برای پیگیری در اختیار تیم پشتیبانی قرار دهید.</p><code dir="ltr">{detail.delivery_request_id}</code><div className="form-actions"><button className="secondary-button" onClick={() => void copyId()}>{copied ? 'شناسه کپی شد' : 'کپی شناسه ارسال'}</button><button className="primary-button" onClick={onClose}>بستن</button></div></section></div>
}

function CancelDialog({ detail, loading, error, onClose, onConfirm }: { detail: DeliveryDetail; loading: boolean; error: string; onClose: () => void; onConfirm: () => void }) {
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !loading) onClose() }}><section className="action-dialog" role="dialog" aria-modal="true" aria-labelledby="cancel-title"><header><div><span className="eyebrow">لغو مأموریت</span><h2 id="cancel-title">درخواست لغو ارسال؟</h2></div><button className="icon-button" onClick={onClose} disabled={loading} aria-label="بستن">×</button></header><p>درخواست لغو برای مأموریت <code dir="ltr">{detail.mission_id}</code> ثبت می‌شود. سامانه وضعیت مجاز را دوباره بررسی می‌کند.</p>{error && <div className="inline-error" role="alert">{error}</div>}<div className="form-actions"><button className="secondary-button" disabled={loading} onClick={onClose}>بازگشت</button><button className="primary-button" disabled={loading} onClick={onConfirm}>{loading ? 'در حال ثبت…' : 'تأیید لغو مأموریت'}</button></div></section></div>
}

export default App
