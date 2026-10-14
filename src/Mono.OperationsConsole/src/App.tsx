import { useCallback, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'

type Offer = {
  offerId?: string
  courierId?: string
  status?: string
  expiresAt?: string | null
  deadlinePassed?: boolean
}

type Attempt = {
  action?: string
  outcome?: string
  createdAt?: string | null
}

type Mission = {
  missionId: string
  customerId: string
  branchId: string
  createdAt: string
  committedDeliveryAt: string
  activeOffer: Offer | null
  attemptCount: number
  latestAttempt: Attempt | null
  deliveryOverdue: boolean
  deliveryWindowState: 'upcoming' | 'due_now' | 'overdue' | string
}

type QueuePage = { items: Mission[]; nextCursor: string | null }
type MissionRecord = {
  missionId: string
  customerId: string
  deliveryRequestId: string
  status: string
  committedDeliveryAt: string
  createdAt: string
}
type MissionPage = { items: MissionRecord[]; nextCursor: string | null }
type MissionFare = { mission_id: string; customer_id: string; delivery_request_id: string; status: string; committed_delivery_at: string; created_at: string; quote_id: string; quote_version: number; distance_meters: number; fare_toman: number; buyer_share_toman: number; seller_share_toman: number; mono_commission_estimate_toman: number; commission_vat_estimate_toman: number; courier_net_estimate_toman: number }
type MissionFarePage = { items: MissionFare[]; next_cursor: string | null }
type MissionTimeline = { events?: Array<{ eventId: string; eventType: string; sequence: number; occurredAt: string }> }
type MissionReadAudit = { items?: Array<{ auditId: string; actorId: string; action: string; missionId: string | null; resultCount: number; occurredAt: string }> }
type CourierSummary = { courier_id: string; mode: string; status: string; preferred_zone_ids: string[]; approved_zone_ids: string[]; primary_zone_id: string | null; created_at: string; updated_at: string }
type CourierPage = { items: CourierSummary[]; next_cursor: string | null }
type CourierDetail = CourierSummary & { created_by: string; trust_transitions: Array<{ transition_id: string; from_status: string; to_status: string; actor_id: string; reason_code: string; evidence_reference: string; occurred_at: string }> }
type CourierReadAudit = { items?: Array<{ audit_id: string; actor_id: string; action: string; courier_id: string | null; result_count: number; occurred_at: string }> }
type CapacityProjection = { zone_id: string; bucket_minutes: number; buckets: Array<{ from: string; to: string; planned_courier_count: number }> }
type AvailabilityPage = { items: Array<{ window_id: string; courier_id: string; zone_id: string; starts_at: string; ends_at: string; status: string; created_by: string; created_at: string }>; next_cursor: string | null }
type ApiError = { error?: { code?: string; message?: string } }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', {
  dateStyle: 'short',
  timeStyle: 'short',
})

function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date)
}

function statusLabel(state: string) {
  if (state === 'overdue') return 'از موعد گذشته'
  if (state === 'due_now') return 'موعد همین حالا'
  return 'در بازه'
}

async function readError(response: Response) {
  try {
    const body = (await response.json()) as ApiError
    return body.error?.message || body.error?.code || `خطای سرور (${response.status})`
  } catch {
    return `پاسخ نامعتبر از سرور (${response.status})`
  }
}

export default function App() {
  const [viewMode, setViewMode] = useState<'dispatch' | 'missions' | 'couriers' | 'zones' | 'finance'>('dispatch')
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [items, setItems] = useState<Mission[]>([])
  const [missionItems, setMissionItems] = useState<MissionRecord[]>([])
  const [courierItems, setCourierItems] = useState<CourierSummary[]>([])
  const [financeItems, setFinanceItems] = useState<MissionFare[]>([])
  const [financeNextCursor, setFinanceNextCursor] = useState<string | null>(null)
  const [financeQuery, setFinanceQuery] = useState('')
  const [financeStatus, setFinanceStatus] = useState('')
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [missionNextCursor, setMissionNextCursor] = useState<string | null>(null)
  const [courierNextCursor, setCourierNextCursor] = useState<string | null>(null)
  const [availabilityNextCursor, setAvailabilityNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [pageIndex, setPageIndex] = useState(0)
  const [query, setQuery] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [hasOffer, setHasOffer] = useState('')
  const [deliveryWindow, setDeliveryWindow] = useState('')
  const [createdBefore, setCreatedBefore] = useState('')
  const [createdAfter, setCreatedAfter] = useState('')
  const [zoneId, setZoneId] = useState('')
  const [capacityFrom, setCapacityFrom] = useState('')
  const [capacityTo, setCapacityTo] = useState('')
  const [bucketMinutes, setBucketMinutes] = useState('30')
  const [capacityProjection, setCapacityProjection] = useState<CapacityProjection | null>(null)
  const [availabilityPage, setAvailabilityPage] = useState<AvailabilityPage | null>(null)
  const [availabilityCourierId, setAvailabilityCourierId] = useState('')
  const [cancelWindowId, setCancelWindowId] = useState<string | null>(null)
  const [cancelReason, setCancelReason] = useState('')
  const [availabilityWriteLoading, setAvailabilityWriteLoading] = useState(false)
  const [availabilityWriteError, setAvailabilityWriteError] = useState('')
  const [availabilityWriteMessage, setAvailabilityWriteMessage] = useState('')
  const [capacityError, setCapacityError] = useState('')
  const [availabilityError, setAvailabilityError] = useState('')
  const [sortBy, setSortBy] = useState('delivery_due')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [connected, setConnected] = useState(false)
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [missionDetail, setMissionDetail] = useState<MissionRecord | null>(null)
  const [missionTimeline, setMissionTimeline] = useState<NonNullable<MissionTimeline['events']>>([])
  const [missionAudit, setMissionAudit] = useState<NonNullable<MissionReadAudit['items']>>([])
  const [courierDetail, setCourierDetail] = useState<CourierDetail | null>(null)
  const [courierAudit, setCourierAudit] = useState<NonNullable<CourierReadAudit['items']>>([])
  const [detailErrors, setDetailErrors] = useState<string[]>([])
  const [timelineTitle, setTimelineTitle] = useState('')
  const [timelineLoading, setTimelineLoading] = useState(false)
  const detailRequestId = useRef(0)
  const availabilityCreateKey = useRef<{ fingerprint: string; key: string } | null>(null)
  const availabilityCancelKey = useRef<{ fingerprint: string; key: string } | null>(null)

  const totals = useMemo(() => ({
    rows: items.length,
    offers: items.filter((item) => item.activeOffer).length,
    overdue: items.filter((item) => item.deliveryOverdue).length,
    lateOffers: items.filter((item) => item.activeOffer?.deadlinePassed).length,
  }), [items])
  const capacityPeak = useMemo(() => Math.max(1, ...(capacityProjection?.buckets.map((bucket) => bucket.planned_courier_count) || [])), [capacityProjection])

  const fetchPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setItems([])
    const params = new URLSearchParams({ page_size: '25', sort_by: sortBy })
    if (query.trim()) params.set('query', query.trim())
    if (customerId.trim()) params.set('customer_id', customerId.trim())
    if (hasOffer) params.set('has_offer', hasOffer)
    if (deliveryWindow) params.set('delivery_window_state', deliveryWindow)
    if (createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/dispatch/queue?${params}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as QueuePage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ صف مأموریت با قرارداد API سازگار نیست.')
      setItems(page.items)
      setNextCursor(page.nextCursor || null)
      setCursorStack(nextStack || [null])
      setPageIndex((nextStack || [null]).length - 1)
      setConnected(true)
      setUpdatedAt(new Date().toISOString())
    } catch (cause) {
      setConnected(false)
      setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.')
    } finally {
      setLoading(false)
    }
  }, [token, sortBy, query, customerId, hasOffer, deliveryWindow, createdBefore])

  const fetchMissionPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setMissionItems([])
    const params = new URLSearchParams({ page_size: '25' })
    if (query.trim()) params.set('query', query.trim())
    if (createdAfter) params.set('created_after', new Date(createdAfter).toISOString())
    if (createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/missions?${params}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as MissionPage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ فهرست مأموریت با قرارداد API سازگار نیست.')
      setMissionItems(page.items)
      setMissionNextCursor(page.nextCursor || null)
      setCursorStack(nextStack || [null])
      setPageIndex((nextStack || [null]).length - 1)
      setConnected(true)
      setUpdatedAt(new Date().toISOString())
    } catch (cause) {
      setConnected(false)
      setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.')
    } finally {
      setLoading(false)
    }
  }, [token, query, createdAfter, createdBefore])

  const fetchCourierPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) { setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.'); return }
    setLoading(true); setError(''); setCourierItems([])
    const params = new URLSearchParams({ page_size: '25' })
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/couriers?${params}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      const page = await response.json() as CourierPage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ فهرست پیک‌ها با قرارداد API سازگار نیست.')
      setCourierItems(page.items); setCourierNextCursor(page.next_cursor || null)
      setCursorStack(nextStack || [null]); setPageIndex((nextStack || [null]).length - 1)
      setConnected(true); setUpdatedAt(new Date().toISOString())
    } catch (cause) { setConnected(false); setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.') }
    finally { setLoading(false) }
  }, [token])

  const fetchFinancePage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) { setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.'); return }
    setLoading(true); setError(''); setFinanceItems([])
    const params = new URLSearchParams({ page_size: '25' })
    if (financeQuery.trim()) params.set('query', financeQuery.trim())
    if (financeStatus) params.set('status', financeStatus)
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/finance/mission-fares?${params}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      const page = await response.json() as MissionFarePage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ بررسی کرایه با قرارداد API سازگار نیست.')
      setFinanceItems(page.items); setFinanceNextCursor(page.next_cursor || null)
      setCursorStack(nextStack || [null]); setPageIndex((nextStack || [null]).length - 1)
      setConnected(true); setUpdatedAt(new Date().toISOString())
    } catch (cause) { setConnected(false); setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.') }
    finally { setLoading(false) }
  }, [token, financeQuery, financeStatus])

  async function submitAvailabilityWindow() {
    if (!token) { setAvailabilityWriteError('ابتدا با توکن مدیر عملیات متصل شوید.'); return }
    const courier = availabilityCourierId.trim()
    if (!/^crr_[0-9a-f]{32}$/.test(courier)) {
      setAvailabilityWriteError('شناسهٔ پیک باید با قالب crr_ و ۳۲ رقم هگزادسیمال وارد شود.')
      return
    }
    if (!zoneId.trim() || !capacityFrom || !capacityTo) {
      setAvailabilityWriteError('زون و زمان شروع و پایان را کامل کنید.')
      return
    }
    const from = new Date(capacityFrom)
    const to = new Date(capacityTo)
    const now = Date.now()
    if (Number.isNaN(from.valueOf()) || Number.isNaN(to.valueOf()) ||
        from.getTime() < now || from.getTime() > now + 30 * 24 * 60 * 60 * 1000 ||
        to <= from || to.getTime() - from.getTime() > 16 * 60 * 60 * 1000) {
      setAvailabilityWriteError('پنجره باید در ۳۰ روز آینده باشد و حداکثر ۱۶ ساعت طول بکشد.')
      return
    }
    const createFingerprint = JSON.stringify({ courier, zone_id: zoneId.trim(), starts_at: from.toISOString(), ends_at: to.toISOString() })
    if (availabilityCreateKey.current?.fingerprint !== createFingerprint) availabilityCreateKey.current = { fingerprint: createFingerprint, key: crypto.randomUUID() }
    setAvailabilityWriteLoading(true); setAvailabilityWriteError(''); setAvailabilityWriteMessage('')
    try {
      const response = await fetch(`/operations/v1/couriers/${encodeURIComponent(courier)}/availability-windows`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json', 'Content-Type': 'application/json', 'Idempotency-Key': availabilityCreateKey.current.key },
        body: JSON.stringify({ zone_id: zoneId.trim(), starts_at: from.toISOString(), ends_at: to.toISOString() }),
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const window = await response.json() as AvailabilityPage['items'][number]
      if (!window?.window_id || window.status !== 'SCHEDULED') throw new Error('ساختار پاسخ ثبت پنجرهٔ کاری معتبر نیست.')
      availabilityCreateKey.current = null
      setAvailabilityWriteMessage('پنجرهٔ کاری با موفقیت ثبت شد. این برنامه‌ریزی به معنی حضور لحظه‌ای یا تخصیص پیک نیست.')
      setAvailabilityPage((current) => {
        if (!current || (zoneId.trim() && window.zone_id !== zoneId.trim()) ||
            new Date(window.starts_at) >= new Date(capacityTo) || new Date(window.ends_at) <= new Date(capacityFrom)) return current
        const rows = [...current.items.filter((item) => item.window_id !== window.window_id), window]
          .sort((left, right) => left.starts_at.localeCompare(right.starts_at) || left.window_id.localeCompare(right.window_id))
        return { ...current, items: rows.slice(0, 50) }
      })
      setAvailabilityCourierId('')
    } catch (cause) {
      setAvailabilityWriteError(cause instanceof Error ? cause.message : 'ثبت پنجرهٔ کاری انجام نشد.')
    } finally { setAvailabilityWriteLoading(false) }
  }

  async function cancelAvailabilityWindow(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!token || !cancelWindowId) return
    const reason = cancelReason.trim()
    if (reason.length < 2 || reason.length > 128 || /[\u0000-\u001f\u007f]/.test(reason)) {
      setAvailabilityWriteError('دلیل لغو باید ۲ تا ۱۲۸ نویسه و بدون نویسهٔ کنترلی باشد.')
      return
    }
    const cancelFingerprint = JSON.stringify({ window_id: cancelWindowId, reason })
    if (availabilityCancelKey.current?.fingerprint !== cancelFingerprint) availabilityCancelKey.current = { fingerprint: cancelFingerprint, key: crypto.randomUUID() }
    setAvailabilityWriteLoading(true); setAvailabilityWriteError(''); setAvailabilityWriteMessage('')
    try {
      const response = await fetch(`/operations/v1/courier-availability-windows/${encodeURIComponent(cancelWindowId)}/cancel`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json', 'Content-Type': 'application/json', 'Idempotency-Key': availabilityCancelKey.current.key },
        body: JSON.stringify({ reason }),
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const window = await response.json() as AvailabilityPage['items'][number]
      if (window?.window_id !== cancelWindowId || window.status !== 'CANCELLED') throw new Error('ساختار پاسخ لغو پنجرهٔ کاری معتبر نیست.')
      setAvailabilityPage((current) => current ? { ...current, items: current.items.map((item) => item.window_id === window.window_id ? window : item) } : current)
      availabilityCancelKey.current = null
      setAvailabilityWriteMessage('پنجرهٔ کاری لغو شد و دلیل در سابقهٔ آن ثبت شد.')
      setCancelWindowId(null); setCancelReason('')
    } catch (cause) {
      setAvailabilityWriteError(cause instanceof Error ? cause.message : 'لغو پنجرهٔ کاری انجام نشد.')
    } finally { setAvailabilityWriteLoading(false) }
  }

  const fetchZoneCapacity = useCallback(async (cursor: string | null, nextStack?: Array<string | null>, refreshProjection = true) => {
    if (!token) { setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.'); return }
    if (!zoneId.trim() || !capacityFrom || !capacityTo) { setError('شناسهٔ زون و بازهٔ زمانی را کامل کنید.'); return }
    const from = new Date(capacityFrom)
    const to = new Date(capacityTo)
    if (Number.isNaN(from.valueOf()) || Number.isNaN(to.valueOf()) || to <= from || to.getTime() - from.getTime() > 24 * 60 * 60 * 1000) {
      setError('بازهٔ زمانی باید معتبر، مثبت و حداکثر ۲۴ ساعت باشد.'); return
    }
    const bucket = Number(bucketMinutes)
    const fromUtc = from.toISOString()
    const toUtc = to.toISOString()
    if (![15, 30, 60].includes(bucket) || new Date(fromUtc).getUTCMinutes() % bucket !== 0 || new Date(toUtc).getUTCMinutes() % bucket !== 0 || (to.getTime() - from.getTime()) / 60000 % bucket !== 0) {
      setError('زمان شروع و پایان در UTC باید با گام انتخاب‌شده هم‌تراز باشند.'); return
    }
    setLoading(true); setError(''); setCapacityError(''); setAvailabilityError('')
    const params = new URLSearchParams({ zone_id: zoneId.trim(), from: fromUtc, to: toUtc, page_size: '50' })
    if (cursor) params.set('cursor', cursor)
    const availabilityRequest = fetch(`/operations/v1/courier-availability-windows?${params}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
    const projectionParams = new URLSearchParams({ zone_id: zoneId.trim(), from: fromUtc, to: toUtc, bucket_minutes: String(bucket) })
    const projectionRequest = refreshProjection
      ? fetch(`/operations/v1/courier-capacity-projection?${projectionParams}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      : Promise.resolve(null)
    const [availabilityResult, projectionResult] = await Promise.allSettled([availabilityRequest, projectionRequest])
    if (availabilityResult.status === 'fulfilled') {
      if (availabilityResult.value.ok) {
        try {
          const page = await availabilityResult.value.json() as AvailabilityPage
          if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ فهرست پنجره‌های برنامه‌ریزی نامعتبر است.')
          setAvailabilityPage(page); setAvailabilityNextCursor(page.next_cursor || null)
        } catch (cause) { setAvailabilityError(cause instanceof Error ? cause.message : 'پاسخ فهرست پنجره‌های برنامه‌ریزی نامعتبر است.'); setAvailabilityPage(null); setAvailabilityNextCursor(null) }
      } else { setAvailabilityError(await readError(availabilityResult.value)); setAvailabilityPage(null); setAvailabilityNextCursor(null) }
    } else { setAvailabilityError('ارتباط با API فهرست پنجره‌های برنامه‌ریزی برقرار نشد.'); setAvailabilityPage(null); setAvailabilityNextCursor(null) }
    if (refreshProjection) {
      if (projectionResult.status === 'fulfilled' && projectionResult.value) {
        if (projectionResult.value.ok) {
          try { const projection = await projectionResult.value.json() as CapacityProjection; if (!Array.isArray(projection.buckets)) throw new Error('ساختار پاسخ ظرفیت نامعتبر است.'); setCapacityProjection(projection) }
          catch (cause) { setCapacityError(cause instanceof Error ? cause.message : 'پاسخ ظرفیت نامعتبر است.'); setCapacityProjection(null) }
        } else { setCapacityError(await readError(projectionResult.value)); setCapacityProjection(null) }
      } else { setCapacityError('ارتباط با API ظرفیت برقرار نشد.'); setCapacityProjection(null) }
    }
    setCursorStack(nextStack || [null]); setPageIndex((nextStack || [null]).length - 1)
    setConnected(true); setUpdatedAt(new Date().toISOString()); setLoading(false)
  }, [token, zoneId, capacityFrom, capacityTo, bucketMinutes])

  function connect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setToken(tokenDraft.trim())
    setTokenDraft('')
    setItems([])
    setMissionItems([])
    setCourierItems([])
    setFinanceItems([])
    setFinanceNextCursor(null)
    setCourierNextCursor(null)
    setAvailabilityNextCursor(null)
    setCapacityProjection(null)
    setAvailabilityPage(null)
    setCancelWindowId(null); setCancelReason(''); setAvailabilityWriteError(''); setAvailabilityWriteMessage('')
    setCursorStack([null])
    setPageIndex(0)
    setConnected(false)
    setError('')
    if (!tokenDraft.trim()) {
      setError('توکن معتبر عملیات را وارد کنید.')
      return
    }
    // State updates are asynchronous; use the submitted values for the first request.
    void requestWithCredentials(tokenDraft.trim(), null, [null])
  }

  async function requestWithCredentials(
    submittedToken: string,
    cursor: string | null,
    stack: Array<string | null>,
  ) {
    setLoading(true)
    const params = new URLSearchParams({ page_size: '25', sort_by: sortBy })
    if (query.trim()) params.set('query', query.trim())
    if (customerId.trim()) params.set('customer_id', customerId.trim())
    if (hasOffer) params.set('has_offer', hasOffer)
    if (deliveryWindow) params.set('delivery_window_state', deliveryWindow)
    if (createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/dispatch/queue?${params}`, {
        headers: { Authorization: `Bearer ${submittedToken}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as QueuePage
      if (!Array.isArray(page.items)) throw new Error('ساختار پاسخ صف مأموریت با قرارداد API سازگار نیست.')
      setItems(page.items)
      setNextCursor(page.nextCursor || null)
      setCursorStack(stack)
      setPageIndex(stack.length - 1)
      setConnected(true)
      setUpdatedAt(new Date().toISOString())
      setError('')
    } catch (cause) {
      setConnected(false)
      setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.')
    } finally {
      setLoading(false)
    }
  }

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (viewMode === 'dispatch') void fetchPage(null, [null])
    else if (viewMode === 'missions') void fetchMissionPage(null, [null])
    else if (viewMode === 'couriers') void fetchCourierPage(null, [null])
    else if (viewMode === 'finance') void fetchFinancePage(null, [null])
    else void fetchZoneCapacity(null, [null], true)
  }

  function goNext() {
    const cursor = viewMode === 'dispatch' ? nextCursor : viewMode === 'missions' ? missionNextCursor : viewMode === 'couriers' ? courierNextCursor : viewMode === 'finance' ? financeNextCursor : availabilityNextCursor
    if (!cursor) return
    const nextStack = [...cursorStack, cursor]
    if (viewMode === 'dispatch') void fetchPage(cursor, nextStack)
    else if (viewMode === 'missions') void fetchMissionPage(cursor, nextStack)
    else if (viewMode === 'couriers') void fetchCourierPage(cursor, nextStack)
    else if (viewMode === 'finance') void fetchFinancePage(cursor, nextStack)
    else void fetchZoneCapacity(cursor, nextStack, false)
  }

  function goPrevious() {
    if (pageIndex < 1) return
    const previousStack = cursorStack.slice(0, -1)
    const cursor = previousStack[previousStack.length - 1] || null
    if (viewMode === 'dispatch') void fetchPage(cursor, previousStack)
    else if (viewMode === 'missions') void fetchMissionPage(cursor, previousStack)
    else if (viewMode === 'couriers') void fetchCourierPage(cursor, previousStack)
    else if (viewMode === 'finance') void fetchFinancePage(cursor, previousStack)
    else void fetchZoneCapacity(cursor, previousStack, false)
  }

  function switchView(mode: 'dispatch' | 'missions' | 'couriers' | 'zones' | 'finance') {
    setViewMode(mode)
    setCursorStack([null])
    setPageIndex(0)
    setError('')
    if (mode === 'dispatch') void fetchPage(null, [null])
    else if (mode === 'missions') void fetchMissionPage(null, [null])
    else if (mode === 'couriers') void fetchCourierPage(null, [null])
    else if (mode === 'finance') void fetchFinancePage(null, [null])
    else { setAvailabilityPage(null); setCapacityProjection(null); setCapacityError(''); setAvailabilityError('') }
  }

  async function showCourierDetails(courierId: string) {
    const requestId = ++detailRequestId.current
    setTimelineTitle(courierId); setCourierDetail(null); setCourierAudit([]); setDetailErrors([]); setTimelineLoading(true)
    const requests = await Promise.allSettled([
      fetch(`/operations/v1/couriers/${encodeURIComponent(courierId)}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch('/operations/v1/courier-read-audit?page_size=100', { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
    ])
    const errors: string[] = []
    const read = async <T,>(result: PromiseSettledResult<Response>, label: string): Promise<T | null> => {
      if (result.status === 'rejected') { errors.push(`${label}: خطای ارتباط`); return null }
      if (!result.value.ok) { errors.push(`${label}: ${await readError(result.value)}`); return null }
      try { return await result.value.json() as T } catch { errors.push(`${label}: قالب پاسخ معتبر نیست.`); return null }
    }
    const [detail, audit] = await Promise.all([read<CourierDetail>(requests[0], 'جزئیات پیک'), read<CourierReadAudit>(requests[1], 'گزارش دسترسی')])
    if (requestId !== detailRequestId.current) return
    setCourierDetail(detail); setCourierAudit(Array.isArray(audit?.items) ? audit.items.filter((row) => row.courier_id === courierId) : []); setDetailErrors(errors); setTimelineLoading(false)
  }

  async function showMissionDetails(missionId: string) {
    const requestId = ++detailRequestId.current
    setTimelineTitle(missionId)
    setMissionDetail(null)
    setCourierDetail(null)
    setCourierAudit([])
    setCourierDetail(null)
    setCourierAudit([])
    setMissionTimeline([])
    setMissionAudit([])
    setDetailErrors([])
    setTimelineLoading(true)
    const missionPath = `/operations/v1/missions/${encodeURIComponent(missionId)}`
    const requests = await Promise.allSettled([
      fetch(missionPath, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch(`${missionPath}/timeline`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch(`/operations/v1/mission-read-audit?page_size=25&mission_id=${encodeURIComponent(missionId)}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store',
      }),
    ])
    const errors: string[] = []
    const readResponse = async <T,>(result: PromiseSettledResult<Response>, label: string): Promise<T | null> => {
      if (result.status === 'rejected') {
        errors.push(`${label}: ${result.reason instanceof Error ? result.reason.message : 'خطای ارتباط'}`)
        return null
      }
      if (!result.value.ok) {
        errors.push(`${label}: ${await readError(result.value)}`)
        return null
      }
      try { return await result.value.json() as T }
      catch { errors.push(`${label}: قالب پاسخ معتبر نیست.`); return null }
    }
    const [detail, timelineResult, auditResult] = await Promise.all([
      readResponse<MissionRecord>(requests[0], 'جزئیات مأموریت'),
      readResponse<MissionTimeline>(requests[1], 'timeline'),
      readResponse<MissionReadAudit>(requests[2], 'گزارش دسترسی'),
    ])
    if (requestId !== detailRequestId.current) return
    setMissionDetail(detail)
    setMissionTimeline(Array.isArray(timelineResult?.events) ? timelineResult.events : [])
    setMissionAudit(Array.isArray(auditResult?.items) ? auditResult.items : [])
    setDetailErrors(errors)
    setTimelineLoading(false)
  }

  function closeDetails() {
    detailRequestId.current += 1
    setTimelineTitle('')
    setTimelineLoading(false)
  }

  function disconnect() {
    setToken('')
    setTokenDraft('')
    setItems([])
    setMissionItems([])
    setCourierItems([])
    setFinanceItems([])
    setFinanceNextCursor(null)
    setCourierNextCursor(null)
    setAvailabilityNextCursor(null)
    setCapacityProjection(null)
    setAvailabilityPage(null)
    setCancelWindowId(null); setCancelReason(''); setAvailabilityWriteError(''); setAvailabilityWriteMessage('')
    setNextCursor(null)
    setCursorStack([null])
    setPageIndex(0)
    setConnected(false)
    setMissionDetail(null)
    setCourierDetail(null)
    setCourierAudit([])
    setMissionTimeline([])
    setMissionAudit([])
    closeDetails()
    setError('')
  }

  return (
    <div className="app-shell" dir="rtl">
      <aside className="sidebar">
        <div className="brand-lockup"><span className="brand-mark">م</span><span><strong>مونو</strong><small>LOGISTICS</small></span></div>
        <div className="side-caption">فضای عملیات</div>
        <nav className="navigation" aria-label="ناوبری اصلی">
          <button className={`nav-item ${viewMode === 'dispatch' ? 'active' : ''}`} onClick={() => switchView('dispatch')}><span className="nav-icon">▦</span>صف dispatch</button>
          <button className={`nav-item ${viewMode === 'missions' ? 'active' : ''}`} onClick={() => switchView('missions')}><span className="nav-icon">↗</span>همهٔ مأموریت‌ها</button>
          <button className={`nav-item ${viewMode === 'couriers' ? 'active' : ''}`} onClick={() => switchView('couriers')}><span className="nav-icon">⌁</span>پیک‌ها</button>
          <button className={`nav-item ${viewMode === 'zones' ? 'active' : ''}`} onClick={() => switchView('zones')}><span className="nav-icon">◉</span>زون‌ها و ظرفیت</button>
          <button className={`nav-item ${viewMode === 'finance' ? 'active' : ''}`} onClick={() => switchView('finance')}><span className="nav-icon">▤</span>بررسی کرایه</button>
        </nav>
        <div className="sidebar-footer"><span className="online-dot" />نسخهٔ عملیاتی داخلی</div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div className="topbar-actions">
            <button className="icon-button" onClick={() => viewMode === 'dispatch' ? void fetchPage(cursorStack[pageIndex] || null) : viewMode === 'missions' ? void fetchMissionPage(cursorStack[pageIndex] || null) : viewMode === 'couriers' ? void fetchCourierPage(cursorStack[pageIndex] || null) : viewMode === 'finance' ? void fetchFinancePage(cursorStack[pageIndex] || null) : void fetchZoneCapacity(cursorStack[pageIndex] || null, undefined, true)} disabled={!token || loading} aria-label="تازه‌سازی">↻</button>
            <span className={`system-pill ${connected ? 'is-online' : ''}`}><i />{connected ? 'اتصال به API برقرار است' : 'اتصال برقرار نیست'}</span>
            {token && <button className="text-button" onClick={disconnect}>خروج</button>}
          </div>
          <div className="page-heading"><h1>مرکز کنترل عملیات</h1><p>{viewMode === 'dispatch' ? 'صف مأموریت‌های در انتظار تخصیص پیک' : viewMode === 'missions' ? 'جست‌وجو و پیگیری مأموریت‌های ثبت‌شده' : viewMode === 'couriers' ? 'فهرست و سوابق اعتماد پیک‌ها' : viewMode === 'finance' ? 'برآورد سهم‌ها از snapshot نرخ ثبت‌شدهٔ مأموریت' : 'برنامهٔ ظرفیت زون‌ها و تحلیل پیک‌ها'}</p></div>
        </header>

        {!token && <section className="connect-card">
          <div><span className="eyebrow">دسترسی امن</span><h2>اتصال به API عملیات</h2><p>توکن معتبر مدیر عملیات را وارد کنید. توکن فقط در حافظهٔ همین صفحه نگهداری می‌شود.</p></div>
          <form className="connect-form" onSubmit={connect}>
            <label>توکن Bearer <input type="password" value={tokenDraft} onChange={(event) => setTokenDraft(event.target.value)} placeholder="توکن دسترسی عملیات" dir="ltr" autoComplete="off" /></label>
            <button className="primary-button" disabled={loading}>اتصال و دریافت داده</button>
          </form>
        </section>}

        {error && <div className="error-banner" role="alert"><span>!</span><div><strong>دریافت اطلاعات ناموفق بود</strong><p>{error}</p></div><button onClick={() => setError('')} aria-label="بستن">×</button></div>}

        {viewMode === 'dispatch' && <section className="kpi-grid" aria-label="خلاصهٔ صفحهٔ جاری">
          <article className="kpi-card"><span className="kpi-icon cyan">▦</span><div><small>مأموریت‌های این صفحه</small><strong>{fa.format(totals.rows)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon blue">↗</span><div><small>پیشنهاد فعال در این صفحه</small><strong>{fa.format(totals.offers)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon amber">◷</span><div><small>موعد تحویل گذشته</small><strong>{fa.format(totals.overdue)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon red">!</span><div><small>پیشنهاد با مهلت گذشته</small><strong>{fa.format(totals.lateOffers)}</strong></div></article>
        </section>}

        <section className="queue-panel">
          <div className="panel-heading"><div><span className="eyebrow">{viewMode === 'dispatch' ? 'مدیریت تخصیص' : 'پایش عملیات'}</span><h2>{viewMode === 'dispatch' ? 'صف انتظار dispatch' : viewMode === 'missions' ? 'همهٔ مأموریت‌ها' : viewMode === 'couriers' ? 'فهرست پیک‌ها' : viewMode === 'finance' ? 'بررسی برآورد کرایهٔ مأموریت' : 'ظرفیت و برنامه‌ریزی زون'}</h2></div><div className="panel-meta">{updatedAt ? `به‌روزرسانی ${formatDate(updatedAt)}` : 'دادهٔ زنده از API'}</div></div>
          {viewMode === 'dispatch' ? <form className="filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="جست‌وجوی شناسهٔ مأموریت" /></label>
            <label><span className="sr-only">وضعیت موعد</span><select value={deliveryWindow} onChange={(event) => setDeliveryWindow(event.target.value)}><option value="">همهٔ موعدها</option><option value="upcoming">در پیش</option><option value="due_now">موعد همین حالا</option><option value="overdue">از موعد گذشته</option></select></label>
            <label><span className="sr-only">پیشنهاد فعال</span><select value={hasOffer} onChange={(event) => setHasOffer(event.target.value)}><option value="">همهٔ پیشنهادها</option><option value="true">دارای پیشنهاد فعال</option><option value="false">بدون پیشنهاد فعال</option></select></label>
            <label><span className="sr-only">ترتیب</span><select value={sortBy} onChange={(event) => setSortBy(event.target.value)}><option value="delivery_due">نزدیک‌ترین موعد</option><option value="created_at">جدیدترین مأموریت</option></select></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
          </form> : viewMode === 'missions' ? <form className="filters mission-filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="شناسه مأموریت، درخواست یا مرجع سفارش" /></label>
            <label>از تاریخ<input type="datetime-local" value={createdAfter} onChange={(event) => setCreatedAfter(event.target.value)} /></label>
            <label>تا تاریخ<input type="datetime-local" value={createdBefore} onChange={(event) => setCreatedBefore(event.target.value)} /></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>جست‌وجو</button>
          </form>  : viewMode === 'couriers' ? <div className="courier-list-note">جست‌وجو در API فهرست پیک‌ها ارائه نشده است؛ این فهرست با صفحه‌بندی API دریافت می‌شود.</div> : viewMode === 'finance' ? <form className="filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={financeQuery} onChange={(event) => setFinanceQuery(event.target.value)} placeholder="شناسهٔ مأموریت، درخواست یا مرجع سفارش" /></label>
            <label><span className="sr-only">وضعیت مأموریت</span><select value={financeStatus} onChange={(event) => setFinanceStatus(event.target.value)}><option value="">همهٔ وضعیت‌ها</option><option value="DELIVERED">تحویل‌شده</option><option value="FAILED">ناموفق</option><option value="CANCELLED">لغوشده</option></select></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
          </form> : <form className="filters capacity-filters" onSubmit={applyFilters}>
            <label>شناسهٔ زون<input value={zoneId} onChange={(event) => setZoneId(event.target.value)} placeholder="شناسهٔ زون عملیاتی فعال" dir="ltr" /></label>
            <label>از زمان<input type="datetime-local" value={capacityFrom} onChange={(event) => setCapacityFrom(event.target.value)} /></label>
            <label>تا زمان<input type="datetime-local" value={capacityTo} onChange={(event) => setCapacityTo(event.target.value)} /></label>
            <label>گام زمانی<select value={bucketMinutes} onChange={(event) => setBucketMinutes(event.target.value)}><option value="15">۱۵ دقیقه</option><option value="30">۳۰ دقیقه</option><option value="60">۶۰ دقیقه</option></select></label>
            <label>شناسهٔ پیک برای برنامه‌ریزی<input value={availabilityCourierId} onChange={(event) => setAvailabilityCourierId(event.target.value)} placeholder="crr_…" dir="ltr" autoComplete="off" /></label>
            <div className="capacity-actions"><button className="secondary-button" type="submit" disabled={!token || loading}>محاسبهٔ ظرفیت</button><button className="primary-button" type="button" onClick={() => void submitAvailabilityWindow()} disabled={!token || loading || availabilityWriteLoading}>ثبت پنجرهٔ کاری</button></div>
          </form>}
          {viewMode === 'zones' && <p className="courier-list-note">زمان‌های ورودی بر اساس منطقهٔ زمانی مرورگر به UTC تبدیل می‌شوند؛ زون باید شناسهٔ زون عملیاتی فعال باشد. ثبت پنجره تا ۳۰ روز آینده و حداکثر ۱۶ ساعت مجاز است.</p>}
          {viewMode === 'zones' && (availabilityWriteError || availabilityWriteMessage) && <div className={availabilityWriteError ? 'detail-errors capacity-errors' : 'availability-write-success'} role={availabilityWriteError ? 'alert' : 'status'}>{availabilityWriteError || availabilityWriteMessage}</div>}
          {viewMode === 'dispatch' && <details className="advanced-filters"><summary>فیلترهای بیشتر</summary><div className="advanced-grid">
            <label>شناسهٔ مشتری<input value={customerId} onChange={(event) => setCustomerId(event.target.value)} placeholder="cus_…" dir="ltr" /></label>
            <label>ایجادشده تا تاریخ<input type="datetime-local" value={createdBefore} onChange={(event) => setCreatedBefore(event.target.value)} /></label>
          </div></details>}

          {viewMode === 'zones' && <>
            {(capacityError || availabilityError) && <div className="detail-errors capacity-errors">{capacityError && <p>ظرفیت برنامه‌ریزی‌شده: {capacityError}</p>}{availabilityError && <p>پنجره‌های برنامه‌ریزی: {availabilityError}</p>}</div>}
            {capacityProjection && <section className="capacity-result" aria-label="ظرفیت برنامه‌ریزی‌شده">
              <div className="capacity-result-heading"><div><strong>ظرفیت برنامه‌ریزی‌شده</strong><small>زون {capacityProjection.zone_id} · هر بازه {fa.format(capacityProjection.bucket_minutes)} دقیقه</small></div><span>{fa.format(capacityProjection.buckets.length)} بازه</span></div>
              <div className="capacity-bars">{capacityProjection.buckets.map((bucket) => {
                return <div className="capacity-bar-column" key={bucket.from} title={`${formatDate(bucket.from)} · ${fa.format(bucket.planned_courier_count)} پیک برنامه‌ریزی‌شده`}><strong>{fa.format(bucket.planned_courier_count)}</strong><span style={{ height: `${Math.max(4, bucket.planned_courier_count / capacityPeak * 76)}px` }} /><small>{new Intl.DateTimeFormat('fa-IR', { hour: '2-digit', minute: '2-digit' }).format(new Date(bucket.from))}</small></div>
              })}</div>
              <p className="data-note">این مقدار فقط از پنجره‌های برنامه‌ریزی‌شده محاسبه شده و نشان‌دهندهٔ حضور لحظه‌ای یا مجوز تخصیص نیست.</p>
            </section>}
          </>}

          <div className="table-wrap">
            <table className={viewMode === 'finance' ? 'finance-table' : undefined}>
              <thead>{viewMode === 'dispatch' ? <tr><th>شناسه مأموریت</th><th>مشتری / شعبه</th><th>زمان ایجاد</th><th>موعد تحویل</th><th>تلاش / آخرین اقدام</th><th>پیشنهاد فعال</th><th>سوابق</th></tr> : viewMode === 'missions' ? <tr><th>شناسه مأموریت</th><th>مشتری</th><th>درخواست</th><th>وضعیت</th><th>موعد تحویل</th><th>زمان ایجاد</th><th>سوابق</th></tr> : viewMode === 'couriers' ? <tr><th>شناسه پیک</th><th>نوع عملیات</th><th>وضعیت اعتماد</th><th>زون‌های ترجیحی</th><th>زون‌های تأییدشده</th><th>زون اصلی</th><th>آخرین تغییر</th><th>جزئیات</th></tr> : viewMode === 'finance' ? <tr><th>مأموریت</th><th>وضعیت</th><th>نرخ ثبت‌شده</th><th>مسافت</th><th>کرایه</th><th>سهم خریدار</th><th>سهم فروشنده</th><th>کمیسیون مونو (برآورد)</th><th>مالیات کمیسیون</th><th>خالص پیک (برآورد)</th></tr> : <tr><th>شناسه پنجره</th><th>شناسه پیک</th><th>زون</th><th>شروع</th><th>پایان</th><th>وضعیت</th><th>ثبت‌کننده</th><th>زمان ثبت</th><th>عملیات</th></tr>}</thead>
              <tbody>
                {loading && <tr><td colSpan={viewMode === 'finance' ? 10 : viewMode === 'zones' ? 9 : 8} className="state-cell"><span className="spinner" />در حال دریافت اطلاعات…</td></tr>}
                {!loading && (viewMode === 'dispatch' ? items.length === 0 : viewMode === 'missions' ? missionItems.length === 0 : viewMode === 'couriers' ? courierItems.length === 0 : viewMode === 'finance' ? financeItems.length === 0 : !availabilityPage?.items.length) && <tr><td colSpan={viewMode === 'finance' ? 10 : 8} className="state-cell"><span className="empty-icon">⌕</span><strong>{token ? viewMode === 'zones' && !availabilityPage ? 'زون و بازه را وارد و محاسبه کنید' : 'رکوردی در این صفحه پیدا نشد' : 'برای شروع، به API متصل شوید'}</strong><small>{token ? 'صفحه را تازه کنید یا به صفحهٔ قبل برگردید.' : 'هیچ دادهٔ نمونه‌ای در این صفحه نمایش داده نمی‌شود.'}</small></td></tr>}
                {!loading && viewMode === 'dispatch' && items.map((mission) => <tr key={mission.missionId}>
                  <td><strong className="mission-id" dir="ltr">{mission.missionId}</strong>{mission.deliveryOverdue && <span className="badge danger">موعد گذشته</span>}</td>
                  <td><span className="cell-primary" dir="ltr">{mission.customerId}</span><span className="cell-secondary" dir="ltr">{mission.branchId}</span></td>
                  <td>{formatDate(mission.createdAt)}</td>
                  <td><span className="cell-primary">{formatDate(mission.committedDeliveryAt)}</span><span className={`badge ${mission.deliveryWindowState === 'overdue' ? 'danger' : mission.deliveryWindowState === 'due_now' ? 'warning' : 'neutral'}`}>{statusLabel(mission.deliveryWindowState)}</span></td>
                  <td><span className="cell-primary">{fa.format(mission.attemptCount)} تلاش</span><span className="cell-secondary">{mission.latestAttempt?.action || mission.latestAttempt?.outcome || 'بدون تلاش ثبت‌شده'}</span></td>
                  <td>{mission.activeOffer ? <><span className="cell-primary" dir="ltr">{mission.activeOffer.courierId || mission.activeOffer.offerId || 'پیشنهاد فعال'}</span><span className={`badge ${mission.activeOffer.deadlinePassed ? 'danger' : 'success'}`}>{mission.activeOffer.deadlinePassed ? 'مهلت پاسخ گذشته' : `مهلت ${formatDate(mission.activeOffer.expiresAt)}`}</span></> : <span className="cell-secondary">پیشنهادی فعال ندارد</span>}</td>
                  <td><button className="history-button" onClick={() => void showMissionDetails(mission.missionId)}>جزئیات</button></td>
                </tr>)}
                {!loading && viewMode === 'missions' && missionItems.map((mission) => <tr key={mission.missionId}>
                  <td><strong className="mission-id" dir="ltr">{mission.missionId}</strong></td>
                  <td><span className="cell-primary" dir="ltr">{mission.customerId}</span></td>
                  <td><span className="cell-primary" dir="ltr">{mission.deliveryRequestId}</span></td>
                  <td><span className="badge neutral">{mission.status}</span></td>
                  <td>{formatDate(mission.committedDeliveryAt)}</td>
                  <td>{formatDate(mission.createdAt)}</td>
                  <td><button className="history-button" onClick={() => void showMissionDetails(mission.missionId)}>جزئیات</button></td>
                </tr>)}
                {!loading && viewMode === 'couriers' && courierItems.map((courier) => <tr key={courier.courier_id}>
                  <td><strong className="mission-id" dir="ltr">{courier.courier_id}</strong></td>
                  <td><span className="cell-primary" dir="ltr">{courier.mode}</span></td>
                  <td><span className={`badge ${courier.status === 'ACTIVE' ? 'success' : 'neutral'}`}>{courier.status}</span></td>
                  <td dir="ltr">{courier.preferred_zone_ids.join('، ') || '—'}</td><td dir="ltr">{courier.approved_zone_ids.join('، ') || '—'}</td>
                  <td dir="ltr">{courier.primary_zone_id || '—'}</td><td>{formatDate(courier.updated_at)}</td>
                  <td><button className="history-button" onClick={() => void showCourierDetails(courier.courier_id)}>جزئیات</button></td>
                </tr>)}
                {!loading && viewMode === 'finance' && financeItems.map((row) => <tr key={row.mission_id}>
                  <td><strong className="mission-id" dir="ltr">{row.mission_id}</strong><span className="cell-secondary" dir="ltr">{row.delivery_request_id}</span></td>
                  <td><span className="badge neutral">{row.status}</span></td>
                  <td><span className="cell-primary" dir="ltr">{row.quote_id}</span><span className="cell-secondary">نسخهٔ {fa.format(row.quote_version)}</span></td>
                  <td>{fa.format(row.distance_meters)} متر</td><td>{fa.format(row.fare_toman)} تومان</td>
                  <td>{fa.format(row.buyer_share_toman)} تومان</td><td>{fa.format(row.seller_share_toman)} تومان</td>
                  <td>{fa.format(row.mono_commission_estimate_toman)} تومان</td><td>{fa.format(row.commission_vat_estimate_toman)} تومان</td><td>{fa.format(row.courier_net_estimate_toman)} تومان</td>
                </tr>)}
                {!loading && viewMode === 'zones' && availabilityPage?.items.map((window) => <tr key={window.window_id}>
                  <td><strong className="mission-id" dir="ltr">{window.window_id}</strong></td><td dir="ltr">{window.courier_id}</td><td dir="ltr">{window.zone_id}</td>
                  <td>{formatDate(window.starts_at)}</td><td>{formatDate(window.ends_at)}</td><td><span className={`badge ${window.status === 'SCHEDULED' ? 'success' : 'neutral'}`}>{window.status}</span></td><td dir="ltr">{window.created_by}</td><td>{formatDate(window.created_at)}</td>
                  <td>{window.status === 'SCHEDULED' ? <button className="history-button" onClick={() => { setCancelWindowId(window.window_id); setCancelReason(''); setAvailabilityWriteError(''); setAvailabilityWriteMessage('') }}>لغو</button> : <span className="cell-secondary">—</span>}</td>
                </tr>)}
                {!loading && viewMode === 'zones' && cancelWindowId && <tr><td colSpan={9}><form className="cancel-window-form" onSubmit={cancelAvailabilityWindow}>
                  <strong>لغو پنجرهٔ {cancelWindowId}</strong><input value={cancelReason} onChange={(event) => setCancelReason(event.target.value)} maxLength={128} placeholder="دلیل الزامی لغو (۲ تا ۱۲۸ نویسه)" aria-label="دلیل لغو" />
                  <button className="primary-button" type="submit" disabled={availabilityWriteLoading || !token}>تأیید لغو</button><button className="secondary-button" type="button" onClick={() => { setCancelWindowId(null); setCancelReason('') }} disabled={availabilityWriteLoading}>انصراف</button>
                </form></td></tr>}
              </tbody>
            </table>
          </div>
          <footer className="table-footer"><span>{fa.format(viewMode === 'dispatch' ? items.length : viewMode === 'missions' ? missionItems.length : viewMode === 'couriers' ? courierItems.length : viewMode === 'finance' ? financeItems.length : availabilityPage?.items.length || 0)} ردیف در این صفحه</span><div className="pagination"><button onClick={goPrevious} disabled={!token || loading || pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(pageIndex + 1)}</span><button onClick={goNext} disabled={!token || loading || !(viewMode === 'dispatch' ? nextCursor : viewMode === 'missions' ? missionNextCursor : viewMode === 'couriers' ? courierNextCursor : viewMode === 'finance' ? financeNextCursor : availabilityNextCursor)}>بعدی</button></div></footer>
        </section>
        {viewMode === 'dispatch' && <p className="data-note">آمار بالا فقط از ردیف‌های صفحهٔ جاری محاسبه شده است؛ API فعلاً شمار کل صف را برنمی‌گرداند.</p>}
        {viewMode === 'zones' && <p className="data-note">این داده‌ها پنجره‌های کاری ثبت‌شده‌اند؛ حضور زنده، ظرفیت لحظه‌ای یا اختیار تخصیص از آن‌ها نتیجه نمی‌شود.</p>}{viewMode === 'finance' && <p className="data-note">این نما فقط از snapshot قطعی تعرفه و مفروضات تجاری Phase 1 برآورد می‌سازد؛ پرداخت، موجودی، برداشت، تسویهٔ واقعی یا وصول از حنا را گزارش نمی‌کند.</p>}
      </main>

      {timelineTitle && <div className="modal-backdrop" role="presentation" onClick={closeDetails}><section className="timeline-modal detail-modal" role="dialog" aria-modal="true" aria-labelledby="timeline-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">جزئیات و سوابق عملیاتی</span><h2 id="timeline-title" dir="ltr">{timelineTitle}</h2></div><button className="icon-button" onClick={closeDetails} aria-label="بستن">×</button></header>{timelineLoading ? <p className="modal-state">در حال دریافت اطلاعات…</p> : viewMode === 'couriers' ? <>
        {detailErrors.length > 0 && <div className="detail-errors">{detailErrors.map((message) => <p key={message}>{message}</p>)}</div>}
        <section className="detail-section"><h3>مشخصات پیک</h3>{courierDetail ? <dl className="detail-grid">
          <div><dt>شناسه پیک</dt><dd dir="ltr">{courierDetail.courier_id}</dd></div><div><dt>نوع عملیات</dt><dd dir="ltr">{courierDetail.mode}</dd></div><div><dt>وضعیت اعتماد</dt><dd>{courierDetail.status}</dd></div>
          <div><dt>زون اصلی</dt><dd dir="ltr">{courierDetail.primary_zone_id || '—'}</dd></div><div><dt>زون‌های ترجیحی</dt><dd dir="ltr">{courierDetail.preferred_zone_ids.join('، ') || '—'}</dd></div><div><dt>زون‌های تأییدشده</dt><dd dir="ltr">{courierDetail.approved_zone_ids.join('، ') || '—'}</dd></div>
          <div><dt>ثبت‌کننده</dt><dd dir="ltr">{courierDetail.created_by}</dd></div><div><dt>زمان ثبت</dt><dd>{formatDate(courierDetail.created_at)}</dd></div><div><dt>آخرین تغییر</dt><dd>{formatDate(courierDetail.updated_at)}</dd></div>
        </dl> : <p className="modal-state">جزئیات پیک در دسترس نیست.</p>}</section>
        <section className="detail-section"><h3>چرخهٔ اعتماد</h3>{courierDetail?.trust_transitions.length ? <ol className="event-list">{courierDetail.trust_transitions.map((event) => <li key={event.transition_id}><span className="event-dot"/><div><strong>{event.from_status} ← {event.to_status}</strong><small>{formatDate(event.occurred_at)} · {event.actor_id} · {event.reason_code}</small></div></li>)}</ol> : <p className="modal-state">انتقال اعتمادی ثبت نشده است.</p>}</section>
        <section className="detail-section"><h3>سوابق خواندن پیک</h3>{courierAudit.length ? <div className="audit-list">{courierAudit.map((record) => <article key={record.audit_id}><div><strong>{record.action}</strong><small>{record.actor_id}</small></div><span>{formatDate(record.occurred_at)}</span><em>{fa.format(record.result_count)} نتیجه</em></article>)}</div> : <p className="modal-state">در ۱۰۰ رویداد اخیر، سابقه‌ای برای این پیک پیدا نشد.</p>}</section>
      </> : <>
        {detailErrors.length > 0 && <div className="detail-errors">{detailErrors.map((message) => <p key={message}>{message}</p>)}</div>}
        <section className="detail-section"><h3>مشخصات مأموریت</h3>{missionDetail ? <dl className="detail-grid">
          <div><dt>وضعیت</dt><dd><span className="badge neutral">{missionDetail.status}</span></dd></div>
          <div><dt>مشتری</dt><dd dir="ltr">{missionDetail.customerId}</dd></div>
          <div><dt>درخواست تحویل</dt><dd dir="ltr">{missionDetail.deliveryRequestId}</dd></div>
          <div><dt>ایجاد</dt><dd>{formatDate(missionDetail.createdAt)}</dd></div>
          <div><dt>موعد تحویل متعهدشده</dt><dd>{formatDate(missionDetail.committedDeliveryAt)}</dd></div>
        </dl> : <p className="modal-state">جزئیات مأموریت در دسترس نیست.</p>}</section>
        <section className="detail-section"><h3>رویدادهای مأموریت</h3>{missionTimeline.length ? <ol className="event-list">{missionTimeline.map((event) => <li key={event.eventId}><span className="event-dot"/><div><strong>{event.eventType}</strong><small>{formatDate(event.occurredAt)} · {fa.format(event.sequence)}</small></div></li>)}</ol> : <p className="modal-state">رویدادی در timeline ثبت نشده است.</p>}</section>
        <section className="detail-section"><h3>سوابق دسترسی عملیات</h3>{missionAudit.length ? <div className="audit-list">{missionAudit.map((record) => <article key={record.auditId}><div><strong>{record.action}</strong><small>{record.actorId}</small></div><span>{formatDate(record.occurredAt)}</span><em>{fa.format(record.resultCount)} نتیجه</em></article>)}</div> : <p className="modal-state">سابقه‌ای برای نمایش برنگشت.</p>}</section>
      </>}</section></div>}
    </div>
  )
}
