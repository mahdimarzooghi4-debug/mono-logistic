import { useCallback, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import PolicyWorkbench from './PolicyWorkbench'
import OnboardingWorkbench from './OnboardingWorkbench'
import AdmissionsWorkbench from './AdmissionsWorkbench'
import DashboardSummaryCards from './DashboardSummaryCards'
import CourierPlanningEvidence from './CourierPlanningEvidence'
import type { DashboardSummaryMetrics } from './DashboardSummaryCards'

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
type SlaAlert = {
  mission_id: string
  customer_id: string
  status: string
  committed_delivery_at: string
  courier_id: string | null
  is_overdue: boolean
}
type SlaAlertPage = { items: SlaAlert[]; next_cursor: string | null; evaluated_at: string }
type CourierPresence = { courier_id: string; status: 'ONLINE' | 'OFFLINE'; zone_id: string | null; updated_at: string | null }
type CourierPresencePage = { items: CourierPresence[]; nextCursor?: string | null; next_cursor?: string | null }
type CourierIssue = { report_id: string; mission_id: string; courier_id: string; issue_code: string; status: 'OPEN' | 'RESOLVED'; reported_at: string; updated_at: string }
type CourierIssuePage = { reports: CourierIssue[]; nextCursor: string | null }
type WorkerSnapshot = { enabled: boolean; state: 'disabled' | 'blocked' | 'starting' | 'healthy' | 'degraded' | string; last_attempt_at: string | null; last_success_at: string | null; last_batch_claimed?: number | null; last_batch_candidates?: number | null; total_batches: number; successful_batches: number; failed_batches: number }
type WorkerStatusState = { mission: WorkerSnapshot | null; webhook: WorkerSnapshot | null }
type WebhookSummary = { total_count: number; pending_count: number; in_progress_count: number; retrying_count: number; succeeded_count: number; dead_lettered_count: number; cancelled_count: number; due_count: number; stale_lease_count: number; dead_lettered_last_24_hours: number; oldest_open_delivery_at: string | null; observed_at: string }
type WebhookDelivery = { delivery_id: string; endpoint_id: string; event_id: string; event_type: string; status: string; attempt_count: number; replay_generation: number; last_http_status: number | null; last_error_code: string | null; created_at: string; last_attempt_at: string | null; next_attempt_at: string | null; completed_at: string | null }
type WebhookPage = { items: WebhookDelivery[]; nextCursor: string | null }
type WebhookDetail = { delivery: WebhookDelivery; attempts: Array<{ attempt_number: number; action: string; http_status: number | null; error_code: string | null; occurred_at: string }> }
type WebhookAuditPage = { items: Array<{ audit_id: string; actor_id: string; action: string; result_count: number; resource_id: string | null; outcome_code: string | null; occurred_at: string }>; next_cursor: string | null }
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

function issueCodeLabel(code: string) {
  const labels: Record<string, string> = { ACCESS_BLOCKED: 'مسیر مسدود', RECIPIENT_UNAVAILABLE: 'گیرنده در دسترس نیست', PACKAGE_MISMATCH: 'عدم تطابق بسته', SAFETY_CONCERN: 'نگرانی ایمنی', OTHER: 'سایر' }
  return labels[code] || code
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
  const [viewMode, setViewMode] = useState<'dispatch' | 'missions' | 'couriers' | 'zones' | 'finance' | 'sla' | 'presence' | 'issues' | 'workers' | 'webhooks' | 'policies' | 'onboarding' | 'admissions'>('dispatch')
  const [policyReloadKey, setPolicyReloadKey] = useState(0)
  const [token, setToken] = useState('')
  const [tokenDraft, setTokenDraft] = useState('')
  const [items, setItems] = useState<Mission[]>([])
  const [dashboardSummary, setDashboardSummary] = useState<DashboardSummaryMetrics | null>(null)
  const [dashboardError, setDashboardError] = useState('')
  const [missionItems, setMissionItems] = useState<MissionRecord[]>([])
  const [slaItems, setSlaItems] = useState<SlaAlert[]>([])
  const [slaNextCursor, setSlaNextCursor] = useState<string | null>(null)
  const [slaMinutesAhead, setSlaMinutesAhead] = useState('0')
  const [slaEvaluatedAt, setSlaEvaluatedAt] = useState<string | null>(null)
  const [presenceItems, setPresenceItems] = useState<CourierPresence[]>([])
  const [presenceNextCursor, setPresenceNextCursor] = useState<string | null>(null)
  const [presenceStatus, setPresenceStatus] = useState('')
  const [presenceZoneId, setPresenceZoneId] = useState('')
  const [issueItems, setIssueItems] = useState<CourierIssue[]>([])
  const [issueNextCursor, setIssueNextCursor] = useState<string | null>(null)
  const [issueStatus, setIssueStatus] = useState('')
  const [issueCode, setIssueCode] = useState('')
  const [issueMissionId, setIssueMissionId] = useState('')
  const [issueCourierId, setIssueCourierId] = useState('')
  const [issueFromAt, setIssueFromAt] = useState('')
  const [issueToAt, setIssueToAt] = useState('')
  const [workerSnapshots, setWorkerSnapshots] = useState<WorkerStatusState>({ mission: null, webhook: null })
  const [workerErrors, setWorkerErrors] = useState<{ mission: string; webhook: string }>({ mission: '', webhook: '' })
  const [webhookSummary, setWebhookSummary] = useState<WebhookSummary | null>(null)
  const [webhookItems, setWebhookItems] = useState<WebhookDelivery[]>([])
  const [webhookNextCursor, setWebhookNextCursor] = useState<string | null>(null)
  const [webhookDetail, setWebhookDetail] = useState<WebhookDetail | null>(null)
  const [webhookDetailError, setWebhookDetailError] = useState('')
  const [webhookAudit, setWebhookAudit] = useState<WebhookAuditPage | null>(null)
  const [webhookTab, setWebhookTab] = useState<'deliveries' | 'audit'>('deliveries')
  const [webhookCustomerId, setWebhookCustomerId] = useState('')
  const [webhookEndpointId, setWebhookEndpointId] = useState('')
  const [webhookEventType, setWebhookEventType] = useState('')
  const [webhookStatus, setWebhookStatus] = useState('')
  const [webhookErrors, setWebhookErrors] = useState({ summary: '', list: '', audit: '' })
  const [webhookAction, setWebhookAction] = useState<{ deliveryId: string; kind: 'replay' | 'cancel' } | null>(null)
  const [webhookActionError, setWebhookActionError] = useState('')
  const [webhookActionMessage, setWebhookActionMessage] = useState('')
  const [webhookActionLoading, setWebhookActionLoading] = useState(false)
  const webhookActionKeys = useRef(new Map<string, string>())
  const [courierItems, setCourierItems] = useState<CourierSummary[]>([])
  const [courierQuery, setCourierQuery] = useState('')
  const [courierStatus, setCourierStatus] = useState('')
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
  const [planningRefreshKey, setPlanningRefreshKey] = useState(0)
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

  const capacityPeak = useMemo(() => Math.max(1, ...(capacityProjection?.buckets.map((bucket) => bucket.planned_courier_count) || [])), [capacityProjection])

  const fetchDashboardSummary = useCallback(async (submittedToken = token) => {
    if (!submittedToken) { setDashboardSummary(null); setDashboardError(''); return }
    setDashboardError('')
    try {
      const response = await fetch('/operations/v1/dashboard/summary?sla_horizon_minutes=15', {
        headers: { Authorization: `Bearer ${submittedToken}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const summary = await response.json() as DashboardSummaryMetrics
      if (!Number.isFinite(summary.active_mission_count) || !Number.isFinite(summary.online_courier_count) ||
          !Number.isFinite(summary.sla_at_risk_count) || !Number.isFinite(summary.unassigned_mission_count))
        throw new Error('ساختار پاسخ خلاصهٔ داشبورد با قرارداد API سازگار نیست.')
      setDashboardSummary(summary)
    } catch (cause) {
      setDashboardSummary(null)
      setDashboardError(cause instanceof Error ? cause.message : 'دریافت خلاصهٔ داشبورد ناموفق بود.')
    }
  }, [token])

  const fetchPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>, resetFilters = false) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setItems([])
    const params = new URLSearchParams({ page_size: '25', sort_by: sortBy })
    if (!resetFilters && query.trim()) params.set('query', query.trim())
    if (!resetFilters && customerId.trim()) params.set('customer_id', customerId.trim())
    if (!resetFilters && hasOffer) params.set('has_offer', hasOffer)
    if (!resetFilters && deliveryWindow) params.set('delivery_window_state', deliveryWindow)
    if (!resetFilters && createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
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

  const fetchMissionPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>, resetFilters = false) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setMissionItems([])
    const params = new URLSearchParams({ page_size: '25' })
    if (!resetFilters && query.trim()) params.set('query', query.trim())
    if (!resetFilters && createdAfter) params.set('created_after', new Date(createdAfter).toISOString())
    if (!resetFilters && createdBefore) params.set('created_before', new Date(createdBefore).toISOString())
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

  const fetchSlaPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>, minutesAhead = slaMinutesAhead) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setSlaItems([])
    const params = new URLSearchParams({ limit: '50', minutes_ahead: minutesAhead })
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/sla/delivery-alerts?${params}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as SlaAlertPage
      if (!Array.isArray(page.items) || typeof page.evaluated_at !== 'string')
        throw new Error('ساختار پاسخ هشدارهای SLA با قرارداد API سازگار نیست.')
      setSlaItems(page.items)
      setSlaNextCursor(page.next_cursor || null)
      setSlaEvaluatedAt(page.evaluated_at)
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
  }, [token, slaMinutesAhead])

  const fetchPresencePage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>, status = presenceStatus, zoneId = presenceZoneId) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setPresenceItems([])
    const params = new URLSearchParams({ limit: '50' })
    if (status) params.set('status', status)
    if (zoneId.trim()) params.set('zone_id', zoneId.trim())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/couriers/presence?${params}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as CourierPresencePage
      if (!Array.isArray(page.items) || page.items.some((item) =>
        typeof item.courier_id !== 'string' || !['ONLINE', 'OFFLINE'].includes(item.status)))
        throw new Error('ساختار پاسخ حضور پیک‌ها با قرارداد API سازگار نیست.')
      setPresenceItems(page.items)
      setPresenceNextCursor(page.nextCursor || page.next_cursor || null)
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
  }, [token, presenceStatus, presenceZoneId])

  const fetchIssuePage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setError('')
    setIssueItems([])
    const params = new URLSearchParams({ limit: '50' })
    if (issueStatus) params.set('status', issueStatus)
    if (issueCode) params.set('issue_code', issueCode)
    if (issueMissionId.trim()) params.set('mission_id', issueMissionId.trim())
    if (issueCourierId.trim()) params.set('courier_id', issueCourierId.trim())
    if (issueFromAt) params.set('from_at', new Date(issueFromAt).toISOString())
    if (issueToAt) params.set('to_at', new Date(issueToAt).toISOString())
    if (cursor) params.set('cursor', cursor)
    try {
      const response = await fetch(`/operations/v1/courier-issues?${params}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      const page = (await response.json()) as CourierIssuePage
      if (!Array.isArray(page.reports) || page.reports.some((item) =>
        typeof item.report_id !== 'string' || typeof item.mission_id !== 'string' ||
        typeof item.courier_id !== 'string' || typeof item.issue_code !== 'string' ||
        !['OPEN', 'RESOLVED'].includes(item.status)))
        throw new Error('ساختار پاسخ صندوق مشکلات با قرارداد API سازگار نیست.')
      setIssueItems(page.reports)
      setIssueNextCursor(page.nextCursor || null)
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
  }, [token, issueStatus, issueCode, issueMissionId, issueCourierId, issueFromAt, issueToAt])


  const fetchWebhookOps = useCallback(async (cursor: string | null = null, nextStack?: Array<string | null>) => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setWebhookErrors({ summary: '', list: '', audit: '' })
    const listParams = new URLSearchParams({ limit: '50' })
    if (webhookCustomerId.trim()) listParams.set('customer_id', webhookCustomerId.trim())
    if (webhookEndpointId.trim()) listParams.set('endpoint_id', webhookEndpointId.trim())
    if (webhookEventType) listParams.set('event_type', webhookEventType)
    if (webhookStatus) listParams.set('status', webhookStatus)
    if (cursor) listParams.set('cursor', cursor)
    const requests = [
      fetch('/operations/v1/webhook-deliveries/summary', { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch(`/operations/v1/webhook-deliveries?${listParams}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch('/operations/v1/webhook-deliveries/audit?limit=50', { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
    ]
    const [summaryResult, listResult, auditResult] = await Promise.allSettled(requests)
    const errors = { summary: '', list: '', audit: '' }
    let succeeded = false
    if (summaryResult.status === 'fulfilled' && summaryResult.value.ok) {
      try {
        const value = await summaryResult.value.json() as WebhookSummary
        if (typeof value.total_count !== 'number' || typeof value.pending_count !== 'number' || typeof value.observed_at !== 'string')
          throw new Error('ساختار خلاصهٔ صف webhook با قرارداد API سازگار نیست.')
        setWebhookSummary(value); succeeded = true
      } catch (cause) { errors.summary = cause instanceof Error ? cause.message : 'پاسخ خلاصه نامعتبر است.' }
    } else errors.summary = summaryResult.status === 'fulfilled' ? await readError(summaryResult.value) : 'ارتباط با API برقرار نشد.'
    if (listResult.status === 'fulfilled' && listResult.value.ok) {
      try {
        const value = await listResult.value.json() as WebhookPage
        if (!Array.isArray(value.items) || value.items.some((item) => typeof item.delivery_id !== 'string' || typeof item.status !== 'string'))
          throw new Error('ساختار فهرست webhook با قرارداد API سازگار نیست.')
        setWebhookItems(value.items); setWebhookNextCursor(value.nextCursor || null); setCursorStack(nextStack || [null]); setPageIndex((nextStack || [null]).length - 1); succeeded = true
      } catch (cause) { errors.list = cause instanceof Error ? cause.message : 'پاسخ فهرست نامعتبر است.' }
    } else errors.list = listResult.status === 'fulfilled' ? await readError(listResult.value) : 'ارتباط با API برقرار نشد.'
    if (auditResult.status === 'fulfilled' && auditResult.value.ok) {
      try {
        const value = await auditResult.value.json() as WebhookAuditPage
        if (!Array.isArray(value.items)) throw new Error('ساختار audit webhook با قرارداد API سازگار نیست.')
        setWebhookAudit(value); succeeded = true
      } catch (cause) { errors.audit = cause instanceof Error ? cause.message : 'پاسخ audit نامعتبر است.' }
    } else errors.audit = auditResult.status === 'fulfilled' ? await readError(auditResult.value) : 'ارتباط با API برقرار نشد.'
    setWebhookErrors(errors)
    setConnected(succeeded)
    setUpdatedAt(new Date().toISOString())
    setLoading(false)
  }, [token, webhookCustomerId, webhookEndpointId, webhookEventType, webhookStatus])

  const showWebhookDetail = useCallback(async (deliveryId: string) => {
    if (!token) return
    setWebhookDetail(null); setWebhookDetailError('')
    try {
      const response = await fetch(`/operations/v1/webhook-deliveries/${encodeURIComponent(deliveryId)}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      const detail = await response.json() as WebhookDetail
      if (!detail.delivery || !Array.isArray(detail.attempts)) throw new Error('ساختار جزئیات webhook با قرارداد API سازگار نیست.')
      setWebhookDetail(detail)
    } catch (cause) { setWebhookDetailError(cause instanceof Error ? cause.message : 'دریافت جزئیات ناموفق بود.') }
  }, [token])

  async function performWebhookAction() {
    if (!token || !webhookAction) return
    const { deliveryId, kind } = webhookAction
    const keyId = `${kind}:${deliveryId}`
    let idempotencyKey = webhookActionKeys.current.get(keyId)
    if (!idempotencyKey) {
      idempotencyKey = `ops-${kind}-${crypto.randomUUID()}`
      webhookActionKeys.current.set(keyId, idempotencyKey)
    }
    setWebhookActionLoading(true); setWebhookActionError(''); setWebhookActionMessage('')
    try {
      const response = await fetch(`/operations/v1/webhook-deliveries/${encodeURIComponent(deliveryId)}/${kind}`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json', 'Idempotency-Key': idempotencyKey },
        cache: 'no-store',
      })
      if (!response.ok) throw new Error(await readError(response))
      webhookActionKeys.current.delete(keyId)
      setWebhookActionMessage(kind === 'replay' ? 'درخواست replay پذیرفته شد؛ نتیجهٔ ارسال در وضعیت بعدی و تاریخچه ثبت می‌شود.' : 'درخواست لغو ثبت شد.')
      setWebhookAction(null)
      await fetchWebhookOps(null)
      await showWebhookDetail(deliveryId)
    } catch (cause) {
      setWebhookActionError(cause instanceof Error ? cause.message : 'عملیات ناموفق بود. در صورت ابهام، تلاش مجدد با همان کلید idempotency انجام می‌شود.')
    } finally { setWebhookActionLoading(false) }
  }

  const fetchWorkerStatus = useCallback(async () => {
    if (!token) {
      setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.')
      return
    }
    setLoading(true)
    setWorkerErrors({ mission: '', webhook: '' })
    const endpoints = {
      mission: '/operations/v1/dispatch/offer-expiry-worker/status',
      webhook: '/operations/v1/webhook-deliveries/worker-status',
    }
    const entries = await Promise.all(Object.entries(endpoints).map(async ([key, path]) => {
      try {
        const response = await fetch(path, {
          headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
          cache: 'no-store',
        })
        if (!response.ok) throw new Error(await readError(response))
        const snapshot = await response.json() as WorkerSnapshot
        if (typeof snapshot.enabled !== 'boolean' || typeof snapshot.state !== 'string' ||
          typeof snapshot.total_batches !== 'number' || typeof snapshot.successful_batches !== 'number' ||
          typeof snapshot.failed_batches !== 'number')
          throw new Error('ساختار پاسخ وضعیت worker با قرارداد API سازگار نیست.')
        return [key, { snapshot, error: '' }] as const
      } catch (cause) {
        return [key, { snapshot: null, error: cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.' }] as const
      }
    }))
    const result = Object.fromEntries(entries)
    const mission = result.mission as { snapshot: WorkerSnapshot | null; error: string }
    const webhook = result.webhook as { snapshot: WorkerSnapshot | null; error: string }
    setWorkerSnapshots({ mission: mission.snapshot, webhook: webhook.snapshot })
    setWorkerErrors({ mission: mission.error, webhook: webhook.error })
    setConnected(Boolean(mission.snapshot || webhook.snapshot))
    setUpdatedAt(new Date().toISOString())
    setLoading(false)
  }, [token])

  const fetchCourierPage = useCallback(async (cursor: string | null, nextStack?: Array<string | null>) => {
    if (!token) { setError('برای مشاهدهٔ دادهٔ واقعی، توکن معتبر عملیات را وارد کنید.'); return }
    setLoading(true); setError(''); setCourierItems([])
    const params = new URLSearchParams({ page_size: '25' })
    if (courierQuery.trim()) params.set('query', courierQuery.trim())
    if (courierStatus) params.set('status', courierStatus)
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
  }, [token, courierQuery, courierStatus])

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
        if ((zoneId.trim() && window.zone_id !== zoneId.trim()) ||
            new Date(window.starts_at) >= new Date(capacityTo) || new Date(window.ends_at) <= new Date(capacityFrom)) return current
        if (!current) return { items: [window], next_cursor: null }
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
    setPlanningRefreshKey((value) => value + 1)
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
    setPresenceItems([])
    setPresenceNextCursor(null)
    setIssueItems([])
    setIssueNextCursor(null)
    setFinanceItems([])
    setFinanceNextCursor(null)
    setCourierNextCursor(null)
    setAvailabilityNextCursor(null)
    setCapacityProjection(null)
    setAvailabilityPage(null)
    availabilityCreateKey.current = null; availabilityCancelKey.current = null
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
      void fetchDashboardSummary(submittedToken)
    } catch (cause) {
      setConnected(false)
      setError(cause instanceof Error ? cause.message : 'ارتباط با API برقرار نشد.')
    } finally {
      setLoading(false)
    }
  }

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (viewMode === 'dispatch') { void fetchPage(null, [null]); void fetchDashboardSummary() }
    else if (viewMode === 'missions') void fetchMissionPage(null, [null])
    else if (viewMode === 'couriers') void fetchCourierPage(null, [null])
    else if (viewMode === 'finance') void fetchFinancePage(null, [null])
    else if (viewMode === 'sla') void fetchSlaPage(null, [null])
    else if (viewMode === 'presence') void fetchPresencePage(null, [null])
    else if (viewMode === 'issues') void fetchIssuePage(null, [null])
    else if (viewMode === 'workers') void fetchWorkerStatus()
    else if (viewMode === 'webhooks') void fetchWebhookOps(null)
    else void fetchZoneCapacity(null, [null], true)
  }

  function goNext() {
    const cursor = viewMode === 'dispatch' ? nextCursor : viewMode === 'missions' ? missionNextCursor : viewMode === 'couriers' ? courierNextCursor : viewMode === 'finance' ? financeNextCursor : viewMode === 'sla' ? slaNextCursor : viewMode === 'presence' ? presenceNextCursor : viewMode === 'issues' ? issueNextCursor : viewMode === 'webhooks' ? webhookNextCursor : availabilityNextCursor
    if (!cursor) return
    const nextStack = [...cursorStack, cursor]
    if (viewMode === 'dispatch') void fetchPage(cursor, nextStack)
    else if (viewMode === 'missions') void fetchMissionPage(cursor, nextStack)
    else if (viewMode === 'couriers') void fetchCourierPage(cursor, nextStack)
    else if (viewMode === 'finance') void fetchFinancePage(cursor, nextStack)
    else if (viewMode === 'sla') void fetchSlaPage(cursor, nextStack)
    else if (viewMode === 'presence') void fetchPresencePage(cursor, nextStack)
    else if (viewMode === 'issues') void fetchIssuePage(cursor, nextStack)
    else if (viewMode === 'workers') return
    else if (viewMode === 'webhooks') { const nextStack = [...cursorStack, webhookNextCursor!]; void fetchWebhookOps(webhookNextCursor, nextStack) }
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
    else if (viewMode === 'sla') void fetchSlaPage(cursor, previousStack)
    else if (viewMode === 'presence') void fetchPresencePage(cursor, previousStack)
    else if (viewMode === 'issues') void fetchIssuePage(cursor, previousStack)
    else if (viewMode === 'workers') return
    else if (viewMode === 'webhooks') void fetchWebhookOps(cursor, previousStack)
    else void fetchZoneCapacity(cursor, previousStack, false)
  }

  function openDashboardPresence() {
    setViewMode('presence')
    setPresenceStatus('ONLINE')
    setPresenceZoneId('')
    setCursorStack([null])
    setPageIndex(0)
    setError('')
    void fetchPresencePage(null, [null], 'ONLINE', '')
  }

  function openDashboardDispatch() {
    setViewMode('dispatch')
    setQuery('')
    setCustomerId('')
    setHasOffer('')
    setDeliveryWindow('')
    setCreatedBefore('')
    setCursorStack([null])
    setPageIndex(0)
    setError('')
    void fetchPage(null, [null], true)
    void fetchDashboardSummary()
  }

  function openDashboardMissions() {
    setViewMode('missions')
    setQuery('')
    setCreatedAfter('')
    setCreatedBefore('')
    setCursorStack([null])
    setPageIndex(0)
    setError('')
    void fetchMissionPage(null, [null], true)
  }

  function openDashboardSla() {
    const horizon = String(dashboardSummary?.sla_horizon_minutes ?? 15)
    setViewMode('sla')
    setSlaMinutesAhead(horizon)
    setCursorStack([null])
    setPageIndex(0)
    setError('')
    void fetchSlaPage(null, [null], horizon)
  }

  function switchView(mode: 'dispatch' | 'missions' | 'couriers' | 'zones' | 'finance' | 'sla' | 'presence' | 'issues' | 'workers' | 'webhooks' | 'policies' | 'onboarding' | 'admissions') {
    setViewMode(mode)
    setCursorStack([null])
    setPageIndex(0)
    setError('')
    if (mode === 'dispatch') { void fetchPage(null, [null]); void fetchDashboardSummary() }
    else if (mode === 'missions') void fetchMissionPage(null, [null])
    else if (mode === 'couriers') void fetchCourierPage(null, [null])
    else if (mode === 'finance') void fetchFinancePage(null, [null])
    else if (mode === 'sla') void fetchSlaPage(null, [null])
    else if (mode === 'presence') void fetchPresencePage(null, [null])
    else if (mode === 'issues') void fetchIssuePage(null, [null])
    else if (mode === 'workers') void fetchWorkerStatus()
    else if (mode === 'webhooks') { setWebhookTab('deliveries'); void fetchWebhookOps(null) }
    else if (mode === 'policies') setPolicyReloadKey((value) => value + 1)
    else if (mode === 'onboarding') setPolicyReloadKey((value) => value + 1)
    else if (mode === 'admissions') setPolicyReloadKey((value) => value + 1)
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
    availabilityCreateKey.current = null; availabilityCancelKey.current = null
    setToken('')
    setTokenDraft('')
    setDashboardSummary(null)
    setDashboardError('')
    setItems([])
    setMissionItems([])
    setCourierItems([])
    setPresenceItems([])
    setPresenceNextCursor(null)
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
          <button className={`nav-item ${viewMode === 'sla' ? 'active' : ''}`} onClick={() => switchView('sla')}><span className="nav-icon">!</span>هشدارهای SLA</button>
          <button className={`nav-item ${viewMode === 'missions' ? 'active' : ''}`} onClick={() => switchView('missions')}><span className="nav-icon">↗</span>همهٔ مأموریت‌ها</button>
          <button className={`nav-item ${viewMode === 'couriers' ? 'active' : ''}`} onClick={() => switchView('couriers')}><span className="nav-icon">⌁</span>پیک‌ها</button>
          <button className={`nav-item ${viewMode === 'presence' ? 'active' : ''}`} onClick={() => switchView('presence')}><span className="nav-icon">●</span>وضعیت حضور پیک‌ها</button>
          <button className={`nav-item ${viewMode === 'issues' ? 'active' : ''}`} onClick={() => switchView('issues')}><span className="nav-icon">!</span>صندوق مشکلات پیک‌ها</button>
          <button className={`nav-item ${viewMode === 'workers' ? 'active' : ''}`} onClick={() => switchView('workers')}><span className="nav-icon">⚙</span>سلامت پردازش‌ها</button>
          <button className={`nav-item ${viewMode === 'webhooks' ? 'active' : ''}`} onClick={() => switchView('webhooks')}><span className="nav-icon">↻</span>وب‌هوک‌های خروجی</button>
          <button className={`nav-item ${viewMode === 'policies' ? 'active' : ''}`} onClick={() => switchView('policies')}><span className="nav-icon">⚖</span>سیاست‌ها و پیکربندی</button>
          <button className={`nav-item ${viewMode === 'admissions' ? 'active' : ''}`} onClick={() => switchView('admissions')}><span className="nav-icon">▤</span>پذیرش مونو</button>
          <button className={`nav-item ${viewMode === 'onboarding' ? 'active' : ''}`} onClick={() => switchView('onboarding')}><span className="nav-icon">＋</span>راه‌اندازی مشتری و شعب</button>
          <button className={`nav-item ${viewMode === 'zones' ? 'active' : ''}`} onClick={() => switchView('zones')}><span className="nav-icon">◉</span>زون‌ها و ظرفیت</button>
          <button className={`nav-item ${viewMode === 'finance' ? 'active' : ''}`} onClick={() => switchView('finance')}><span className="nav-icon">▤</span>بررسی کرایه</button>
        </nav>
        <div className="sidebar-footer"><span className="online-dot" />نسخهٔ عملیاتی داخلی</div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div className="topbar-actions">
            <button className="icon-button" onClick={() => viewMode === 'dispatch' ? (void fetchPage(cursorStack[pageIndex] || null), void fetchDashboardSummary()) : viewMode === 'missions' ? void fetchMissionPage(cursorStack[pageIndex] || null) : viewMode === 'couriers' ? void fetchCourierPage(cursorStack[pageIndex] || null) : viewMode === 'presence' ? void fetchPresencePage(cursorStack[pageIndex] || null) : viewMode === 'issues' ? void fetchIssuePage(cursorStack[pageIndex] || null) : viewMode === 'finance' ? void fetchFinancePage(cursorStack[pageIndex] || null) : viewMode === 'sla' ? void fetchSlaPage(cursorStack[pageIndex] || null)  : viewMode === 'workers' ? void fetchWorkerStatus() : viewMode === 'webhooks' ? void fetchWebhookOps(cursorStack[pageIndex] || null) : viewMode === 'policies' || viewMode === 'onboarding' || viewMode === 'admissions' ? setPolicyReloadKey((value) => value + 1) : void fetchZoneCapacity(cursorStack[pageIndex] || null, undefined, true)} disabled={!token || loading} aria-label="تازه‌سازی">↻</button>
            <span className={`system-pill ${connected ? 'is-online' : ''}`}><i />{connected ? 'اتصال به API برقرار است' : 'اتصال برقرار نیست'}</span>
            {token && <button className="text-button" onClick={disconnect}>خروج</button>}
          </div>
          <div className="page-heading"><h1>{viewMode === 'policies' ? 'سیاست‌ها و پیکربندی' : viewMode === 'onboarding' ? 'راه‌اندازی مشتری و شعب' : viewMode === 'admissions' ? 'پذیرش مونو' : 'مرکز کنترل عملیات'}</h1><p>{viewMode === 'dispatch' ? 'صف مأموریت‌های در انتظار تخصیص پیک' : viewMode === 'missions' ? 'جست‌وجو و پیگیری مأموریت‌های ثبت‌شده' : viewMode === 'couriers' ? 'فهرست و سوابق اعتماد پیک‌ها' : viewMode === 'presence' ? 'وضعیت آنلاین/آفلاین و آخرین زمان ثبت حضور' : viewMode === 'issues' ? 'گزارش‌های ساختاریافتهٔ مشکلات ثبت‌شده توسط پیک‌ها' : viewMode === 'finance' ? 'برآورد سهم‌ها از snapshot نرخ ثبت‌شدهٔ مأموریت'  : viewMode === 'sla' ? 'مأموریت‌های فعال نزدیک موعد یا عبورکرده از SLA'  : viewMode === 'workers' ? 'وضعیت workerهای پردازش در همین نمونهٔ API' : viewMode === 'webhooks' ? 'پایش صف تحویل و بررسی تلاش‌های ارسال webhook' : viewMode === 'policies' ? 'ثبت و بررسی نسخه‌های سیاست با دسترسی مدیر Mono' : viewMode === 'onboarding' ? 'ثبت و بررسی مشتری‌ها و شعب با مجوز مدیر و سابقهٔ ممیزی API' : viewMode === 'admissions' ? 'نمای یکپارچهٔ صف پیک‌ها و کسب‌وکارها' : 'برنامهٔ ظرفیت زون‌ها و تحلیل پیک‌ها'}</p></div>
        </header>

        {!token && <section className="connect-card">
          <div><span className="eyebrow">دسترسی امن</span><h2>اتصال به API عملیات</h2><p>توکن معتبر مدیر عملیات را وارد کنید. توکن فقط در حافظهٔ همین صفحه نگهداری می‌شود.</p></div>
          <form className="connect-form" onSubmit={connect}>
            <label>توکن Bearer <input type="password" value={tokenDraft} onChange={(event) => setTokenDraft(event.target.value)} placeholder="توکن دسترسی عملیات" dir="ltr" autoComplete="off" /></label>
            <button className="primary-button" disabled={loading}>اتصال و دریافت داده</button>
          </form>
        </section>}

        {error && <div className="error-banner" role="alert"><span>!</span><div><strong>دریافت اطلاعات ناموفق بود</strong><p>{error}</p></div><button onClick={() => setError('')} aria-label="بستن">×</button></div>}
        {viewMode === 'dispatch' && dashboardError && <div className="error-banner" role="alert"><span>!</span><div><strong>خلاصهٔ سراسری داشبورد دریافت نشد</strong><p>{dashboardError}</p></div><button onClick={() => setDashboardError('')} aria-label="بستن">×</button></div>}

        {viewMode === 'dispatch' && <DashboardSummaryCards summary={dashboardSummary} actionsDisabled={!token} onOpenPresence={openDashboardPresence} onOpenDispatch={openDashboardDispatch} onOpenSla={openDashboardSla} onOpenMissions={openDashboardMissions} />}
        {viewMode === 'presence' && <section className="kpi-grid" aria-label="خلاصهٔ حضور پیک‌های همین صفحه">
          <article className="kpi-card"><span className="kpi-icon cyan">●</span><div><small>آنلاین در این صفحه</small><strong>{fa.format(presenceItems.filter((item) => item.status === 'ONLINE').length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon amber">◯</span><div><small>آفلاین در این صفحه</small><strong>{fa.format(presenceItems.filter((item) => item.status === 'OFFLINE').length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon blue">⌁</span><div><small>زون‌های مشخص در این صفحه</small><strong>{fa.format(new Set(presenceItems.map((item) => item.zone_id).filter(Boolean)).size)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon red">◷</span><div><small>بدون زمان حضور</small><strong>{fa.format(presenceItems.filter((item) => !item.updated_at).length)}</strong></div></article>
        </section>}
        {viewMode === 'issues' && <section className="kpi-grid" aria-label="خلاصهٔ صندوق مشکلات همین صفحه">
          <article className="kpi-card"><span className="kpi-icon cyan">!</span><div><small>گزارش‌ها در این صفحه</small><strong>{fa.format(issueItems.length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon amber">◷</span><div><small>باز در این صفحه</small><strong>{fa.format(issueItems.filter((item) => item.status === 'OPEN').length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon blue">✓</span><div><small>رسیدگی‌شده در این صفحه</small><strong>{fa.format(issueItems.filter((item) => item.status === 'RESOLVED').length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon red">!</span><div><small>گزارش نگرانی ایمنی</small><strong>{fa.format(issueItems.filter((item) => item.issue_code === 'SAFETY_CONCERN').length)}</strong></div></article>
        </section>}
        {viewMode === 'sla' && <section className="kpi-grid" aria-label="خلاصهٔ هشدارهای SLA این صفحه">
          <article className="kpi-card"><span className="kpi-icon cyan">!</span><div><small>هشدارهای این صفحه</small><strong>{fa.format(slaItems.length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon red">◷</span><div><small>عبورکرده از موعد</small><strong>{fa.format(slaItems.filter((item) => item.is_overdue).length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon amber">⌁</span><div><small>بدون پیک پذیرفته‌شده</small><strong>{fa.format(slaItems.filter((item) => !item.courier_id).length)}</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon blue">✓</span><div><small>دارای پیک پذیرفته‌شده</small><strong>{fa.format(slaItems.filter((item) => Boolean(item.courier_id)).length)}</strong></div></article>
        </section>}

        {viewMode === 'workers' && <section className="worker-grid" aria-label="وضعیت پردازش‌های پس‌زمینه">
          {([
            ['mission', 'انقضای پیشنهادهای پیک', '/operations/v1/dispatch/offer-expiry-worker/status'],
            ['webhook', 'ارسال webhookها', '/operations/v1/webhook-deliveries/worker-status'],
          ] as const).map(([key, title, endpoint]) => {
            const worker = workerSnapshots[key]
            const errorText = workerErrors[key]
            const stateLabels: Record<string, string> = { disabled: 'غیرفعال', blocked: 'مسدود', starting: 'در حال شروع', healthy: 'سالم', degraded: 'دارای خطا' }
            return <article className="worker-card" key={key}>
              <header><div><span className="eyebrow">وضعیت سرویس پس‌زمینه</span><h2>{title}</h2></div>
                {worker && <span className={`badge ${worker.state === 'healthy' ? 'success' : worker.state === 'degraded' || worker.state === 'blocked' ? 'danger' : 'neutral'}`}>{stateLabels[worker.state] || worker.state}</span>}
              </header>
              {errorText ? <p className="worker-error" role="alert">دریافت وضعیت ناموفق بود: {errorText}</p> : worker ? <>
                <dl className="worker-details">
                  <div><dt>تنظیم فعال</dt><dd>{worker.enabled ? 'بله' : 'خیر'}</dd></div>
                  <div><dt>آخرین تلاش</dt><dd>{formatDate(worker.last_attempt_at)}</dd></div>
                  <div><dt>آخرین موفقیت</dt><dd>{formatDate(worker.last_success_at)}</dd></div>
                  <div><dt>آخرین batch</dt><dd>{fa.format(worker.last_batch_claimed ?? worker.last_batch_candidates ?? 0)}</dd></div>
                </dl>
                <div className="worker-counts"><span>کل batchها <strong>{fa.format(worker.total_batches)}</strong></span><span>موفق <strong>{fa.format(worker.successful_batches)}</strong></span><span>ناموفق <strong>{fa.format(worker.failed_batches)}</strong></span></div>
              </> : <p className="modal-state">برای دریافت وضعیت، صفحه را تازه‌سازی کنید.</p>}
              <code dir="ltr">{endpoint}</code>
            </article>
          })}
        </section>}
        {viewMode === 'webhooks' && <div className="webhook-dashboard">
          {webhookSummary && <section className="kpi-grid" aria-label="خلاصهٔ سراسری صف webhook">
            <article className="kpi-card"><span className="kpi-icon cyan">↻</span><div><small>کل تحویل‌ها</small><strong>{fa.format(webhookSummary.total_count)}</strong></div></article>
            <article className="kpi-card"><span className="kpi-icon amber">◷</span><div><small>موعد ارسال</small><strong>{fa.format(webhookSummary.due_count)}</strong></div></article>
            <article className="kpi-card"><span className="kpi-icon red">!</span><div><small>قفل منقضی</small><strong>{fa.format(webhookSummary.stale_lease_count)}</strong></div></article>
            <article className="kpi-card"><span className="kpi-icon blue">▤</span><div><small>Dead-letter در ۲۴ ساعت</small><strong>{fa.format(webhookSummary.dead_lettered_last_24_hours)}</strong></div></article>
          </section>}
          {webhookSummary && <p className="data-note">خلاصهٔ کل صف، مبنای مشاهده: {formatDate(webhookSummary.observed_at)} · باز: {fa.format(webhookSummary.pending_count + webhookSummary.in_progress_count + webhookSummary.retrying_count)} · موفق: {fa.format(webhookSummary.succeeded_count)} · لغوشده: {fa.format(webhookSummary.cancelled_count)} · قدیمی‌ترین کار باز: {formatDate(webhookSummary.oldest_open_delivery_at)}</p>}
          {Object.entries(webhookErrors).filter(([, message]) => message).map(([key, message]) => <div className="webhook-inline-error" role="alert" key={key}>{key === 'summary' ? 'خلاصهٔ صف' : key === 'list' ? 'فهرست تحویل‌ها' : 'audit'}: {message}</div>)}
          {webhookActionMessage && <div className="availability-write-success" role="status">{webhookActionMessage}</div>}
          <section className="queue-panel webhook-panel">
            <div className="panel-heading"><div><span className="eyebrow">پایش وب‌هوک خروجی</span><h2>تحویل‌ها و تاریخچهٔ عملیات</h2></div><span className="panel-meta">محتوای payload، URL مقصد و secret نمایش داده نمی‌شود</span></div>
            <div className="webhook-tabs" role="tablist" aria-label="نمای وب‌هوک">
              <button type="button" className={webhookTab === 'deliveries' ? 'active' : ''} onClick={() => setWebhookTab('deliveries')}>تحویل‌ها</button>
              <button type="button" className={webhookTab === 'audit' ? 'active' : ''} onClick={() => setWebhookTab('audit')}>سوابق عملیات</button>
            </div>
            {webhookTab === 'deliveries' ? <>
              <form className="filters webhook-filters" onSubmit={applyFilters}>
                <label>شناسهٔ مشتری<input value={webhookCustomerId} onChange={(event) => setWebhookCustomerId(event.target.value)} placeholder="cus_…" dir="ltr" autoComplete="off" /></label>
                <label>شناسهٔ endpoint<input value={webhookEndpointId} onChange={(event) => setWebhookEndpointId(event.target.value)} placeholder="whk_…" dir="ltr" autoComplete="off" /></label>
                <label>نوع رویداد<input value={webhookEventType} onChange={(event) => setWebhookEventType(event.target.value)} placeholder="mission.delivered" dir="ltr" autoComplete="off" /></label>
                <label><span className="sr-only">وضعیت تحویل</span><select value={webhookStatus} onChange={(event) => setWebhookStatus(event.target.value)}><option value="">همهٔ وضعیت‌ها</option><option value="PENDING">در صف</option><option value="IN_PROGRESS">در حال ارسال</option><option value="RETRYING">تلاش مجدد</option><option value="SUCCEEDED">موفق</option><option value="DEAD_LETTERED">ناموفق نهایی</option><option value="CANCELLED">لغوشده</option></select></label>
                <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
              </form>
              <div className="table-wrap"><table className="webhook-table"><thead><tr><th>شناسهٔ تحویل</th><th>رویداد</th><th>وضعیت</th><th>تلاش</th><th>آخرین HTTP</th><th>کد خطا</th><th>ایجاد</th><th>اقدام</th></tr></thead><tbody>
                {loading && <tr><td className="state-cell" colSpan={8}><span className="spinner"/>در حال دریافت اطلاعات…</td></tr>}
                {!loading && webhookItems.map((item) => <tr key={item.delivery_id}>
                  <td><strong className="mission-id" dir="ltr">{item.delivery_id}</strong><span className="cell-secondary" dir="ltr">{item.endpoint_id}</span></td>
                  <td><span className="cell-primary" dir="ltr">{item.event_type}</span><span className="cell-secondary" dir="ltr">event {item.event_id}</span></td>
                  <td><span className={`badge ${item.status === 'SUCCEEDED' ? 'success' : item.status === 'DEAD_LETTERED' ? 'danger' : item.status === 'CANCELLED' ? 'neutral' : 'warning'}`}>{item.status}</span></td>
                  <td>{fa.format(item.attempt_count)}<span className="cell-secondary">نسل replay: {fa.format(item.replay_generation)}</span></td>
                  <td>{item.last_http_status ?? '—'}</td><td dir="ltr">{item.last_error_code || '—'}</td><td>{formatDate(item.created_at)}</td>
                  <td><div className="webhook-row-actions"><button className="history-button" onClick={() => void showWebhookDetail(item.delivery_id)}>جزئیات</button>
                    {item.status === 'DEAD_LETTERED' && <button className="history-button" onClick={() => { setWebhookAction({ deliveryId: item.delivery_id, kind: 'replay' }); setWebhookActionError('') }}>Replay</button>}
                    {(item.status === 'PENDING' || item.status === 'RETRYING') && <button className="history-button danger-action" onClick={() => { setWebhookAction({ deliveryId: item.delivery_id, kind: 'cancel' }); setWebhookActionError('') }}>لغو</button>}
                  </div></td>
                </tr>)}
                {!loading && !webhookErrors.list && webhookItems.length === 0 && <tr><td className="state-cell" colSpan={8}><span className="empty-icon">⌕</span><strong>تحویلی پیدا نشد</strong><small>فیلترها را بررسی کنید یا صف را تازه‌سازی کنید.</small></td></tr>}
              </tbody></table></div>
              <footer className="table-footer"><span>{fa.format(webhookItems.length)} ردیف در این صفحه</span><div className="pagination"><button onClick={goPrevious} disabled={!token || loading || pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(pageIndex + 1)}</span><button onClick={goNext} disabled={!token || loading || !webhookNextCursor}>بعدی</button></div></footer>
            </> : <div className="table-wrap"><table className="webhook-table"><thead><tr><th>زمان</th><th>کاربر عملیات</th><th>عمل</th><th>شناسهٔ منبع</th><th>نتیجه</th></tr></thead><tbody>
              {webhookAudit?.items.map((item) => <tr key={item.audit_id}><td>{formatDate(item.occurred_at)}</td><td dir="ltr">{item.actor_id}</td><td dir="ltr">{item.action}</td><td dir="ltr">{item.resource_id || '—'}</td><td dir="ltr">{item.outcome_code || fa.format(item.result_count)}</td></tr>)}
              {(!webhookAudit || webhookAudit.items.length === 0) && <tr><td className="state-cell" colSpan={5}><strong>رکورد audit برای این صفحه دریافت نشد</strong></td></tr>}
            </tbody></table><p className="data-note webhook-audit-note">نمای audit شامل ۵۰ رکورد نخست است.</p></div>}
          </section>
          {webhookDetail && <section className="queue-panel webhook-detail">
            <div className="panel-heading"><div><span className="eyebrow">جزئیات امن تحویل</span><h2 dir="ltr">{webhookDetail.delivery.delivery_id}</h2></div><button className="text-button" onClick={() => setWebhookDetail(null)}>بستن جزئیات</button></div>
            <dl className="worker-details webhook-meta">
              <div><dt>Endpoint</dt><dd dir="ltr">{webhookDetail.delivery.endpoint_id}</dd></div><div><dt>Event</dt><dd dir="ltr">{webhookDetail.delivery.event_id} · {webhookDetail.delivery.event_type}</dd></div>
              <div><dt>وضعیت</dt><dd>{webhookDetail.delivery.status}</dd></div><div><dt>تلاش‌ها</dt><dd>{fa.format(webhookDetail.delivery.attempt_count)} · replay {fa.format(webhookDetail.delivery.replay_generation)}</dd></div>
              <div><dt>آخرین تلاش</dt><dd>{formatDate(webhookDetail.delivery.last_attempt_at)}</dd></div><div><dt>تلاش بعدی</dt><dd>{formatDate(webhookDetail.delivery.next_attempt_at)}</dd></div>
              <div><dt>پایان</dt><dd>{formatDate(webhookDetail.delivery.completed_at)}</dd></div><div><dt>HTTP / خطا</dt><dd>{webhookDetail.delivery.last_http_status ?? '—'} · {webhookDetail.delivery.last_error_code || '—'}</dd></div>
            </dl>
            {webhookDetailError && <p className="webhook-inline-error">{webhookDetailError}</p>}
            <div className="table-wrap"><table className="webhook-table"><thead><tr><th>#</th><th>عمل</th><th>HTTP</th><th>کد خطا</th><th>زمان</th></tr></thead><tbody>
              {webhookDetail.attempts.map((attempt, index) => <tr key={`${attempt.attempt_number}-${index}`}><td>{fa.format(attempt.attempt_number)}</td><td dir="ltr">{attempt.action}</td><td>{attempt.http_status ?? '—'}</td><td dir="ltr">{attempt.error_code || '—'}</td><td>{formatDate(attempt.occurred_at)}</td></tr>)}
              {webhookDetail.attempts.length === 0 && <tr><td className="state-cell" colSpan={5}>تلاشی در تاریخچه ثبت نشده است.</td></tr>}
            </tbody></table></div>
          </section>}
          {webhookDetailError && !webhookDetail && <div className="webhook-inline-error" role="alert">{webhookDetailError}</div>}
        </div>}
        {viewMode === 'policies' && <PolicyWorkbench token={token} reloadKey={policyReloadKey} onConnected={setConnected} />}
        {viewMode === 'admissions' && <AdmissionsWorkbench token={token} reloadKey={policyReloadKey} onConnected={setConnected} onOpenBusinesses={() => switchView('onboarding')} onOpenCouriers={() => switchView('couriers')} />}
        {viewMode === 'onboarding' && <OnboardingWorkbench token={token} reloadKey={policyReloadKey} onConnected={setConnected} />}
        {viewMode !== 'workers' && viewMode !== 'webhooks' && viewMode !== 'policies' && viewMode !== 'onboarding' && viewMode !== 'admissions' && <section className="queue-panel">
          <div className="panel-heading"><div><span className="eyebrow">{viewMode === 'dispatch' ? 'مدیریت تخصیص' : 'پایش عملیات'}</span><h2>{viewMode === 'dispatch' ? 'صف انتظار dispatch' : viewMode === 'missions' ? 'همهٔ مأموریت‌ها' : viewMode === 'couriers' ? 'فهرست پیک‌ها' : viewMode === 'finance' ? 'بررسی برآورد کرایهٔ مأموریت' : viewMode === 'sla' ? 'فید هشدارهای تحویل' : viewMode === 'presence' ? 'فید حضور پیک‌ها' : viewMode === 'issues' ? 'گزارش مشکلات پیک‌ها' : 'ظرفیت و برنامه‌ریزی زون'}</h2></div><div className="panel-meta">{viewMode === 'sla' && slaEvaluatedAt ? `مبنای ارزیابی ${formatDate(slaEvaluatedAt)}` : updatedAt ? `به‌روزرسانی ${formatDate(updatedAt)}` : 'دادهٔ زنده از API'}</div></div>
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
          </form>  : viewMode === 'couriers' ? <form className="filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={courierQuery} onChange={(event) => setCourierQuery(event.target.value)} placeholder="پیشوند شناسهٔ پیک (crr_…)" dir="ltr" autoComplete="off" /></label>
            <label><span className="sr-only">وضعیت اعتماد پیک</span><select value={courierStatus} onChange={(event) => setCourierStatus(event.target.value)}><option value="">همهٔ وضعیت‌ها</option><option value="REGISTERED">ثبت‌شده</option><option value="IDENTITY_VERIFIED">هویت تأییدشده</option><option value="DOCUMENTS_SUBMITTED">مدارک ارسال‌شده</option><option value="TRUST_REQUIREMENTS_VERIFIED">شرایط اعتماد تأییدشده</option><option value="MONO_APPROVED">تأییدشده توسط مونو</option><option value="ACTIVE">فعال</option></select></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>جست‌وجو</button>
          </form> : viewMode === 'finance' ? <form className="filters" onSubmit={applyFilters}>
            <label className="search-field"><span>⌕</span><input value={financeQuery} onChange={(event) => setFinanceQuery(event.target.value)} placeholder="شناسهٔ مأموریت، درخواست یا مرجع سفارش" /></label>
            <label><span className="sr-only">وضعیت مأموریت</span><select value={financeStatus} onChange={(event) => setFinanceStatus(event.target.value)}><option value="">همهٔ وضعیت‌ها</option><option value="DELIVERED">تحویل‌شده</option><option value="FAILED">ناموفق</option><option value="CANCELLED">لغوشده</option></select></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
          </form> : viewMode === 'issues' ? <form className="filters issue-filters" onSubmit={applyFilters}>
            <label><span className="sr-only">وضعیت گزارش</span><select value={issueStatus} onChange={(event) => setIssueStatus(event.target.value)}><option value="">همهٔ وضعیت‌ها</option><option value="OPEN">باز</option><option value="RESOLVED">رسیدگی‌شده</option></select></label>
            <label><span className="sr-only">نوع مشکل</span><select value={issueCode} onChange={(event) => setIssueCode(event.target.value)}><option value="">همهٔ انواع</option><option value="ACCESS_BLOCKED">مسیر مسدود</option><option value="RECIPIENT_UNAVAILABLE">گیرنده در دسترس نیست</option><option value="PACKAGE_MISMATCH">عدم تطابق بسته</option><option value="SAFETY_CONCERN">نگرانی ایمنی</option><option value="OTHER">سایر</option></select></label>
            <label>شناسهٔ مأموریت<input value={issueMissionId} onChange={(event) => setIssueMissionId(event.target.value)} placeholder="mis_…" dir="ltr" autoComplete="off" /></label>
            <label>شناسهٔ پیک<input value={issueCourierId} onChange={(event) => setIssueCourierId(event.target.value)} placeholder="crr_…" dir="ltr" autoComplete="off" /></label>
            <label>از تاریخ<input type="datetime-local" value={issueFromAt} onChange={(event) => setIssueFromAt(event.target.value)} /></label>
            <label>تا تاریخ<input type="datetime-local" value={issueToAt} onChange={(event) => setIssueToAt(event.target.value)} /></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
          </form> : viewMode === 'presence' ? <form className="filters" onSubmit={applyFilters}>
            <label><span className="sr-only">وضعیت حضور</span><select value={presenceStatus} onChange={(event) => setPresenceStatus(event.target.value)}><option value="">همهٔ وضعیت‌ها</option><option value="ONLINE">آنلاین</option><option value="OFFLINE">آفلاین</option></select></label>
            <label>شناسهٔ زون<input value={presenceZoneId} onChange={(event) => setPresenceZoneId(event.target.value)} placeholder="زون عملیاتی" dir="ltr" autoComplete="off" /></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال فیلتر</button>
          </form> : viewMode === 'sla' ? <form className="filters" onSubmit={applyFilters}>
            <label>هشدار در بازهٔ زمانی<select value={slaMinutesAhead} onChange={(event) => setSlaMinutesAhead(event.target.value)}><option value="0">موعد رسیده یا گذشته</option><option value="15">۱۵ دقیقهٔ آینده</option><option value="30">۳۰ دقیقهٔ آینده</option><option value="60">یک ساعت آینده</option><option value="240">چهار ساعت آینده</option></select></label>
            <button className="secondary-button" type="submit" disabled={!token || loading}>اعمال بازه</button>
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
            <CourierPlanningEvidence token={token} zoneId={zoneId} fromLocal={capacityFrom} toLocal={capacityTo} refreshKey={planningRefreshKey} />
          </>}

          <div className="table-wrap">
            <table className={viewMode === 'finance' ? 'finance-table' : undefined}>
              <thead>{viewMode === 'dispatch' ? <tr><th>شناسه مأموریت</th><th>مشتری / شعبه</th><th>زمان ایجاد</th><th>موعد تحویل</th><th>تلاش / آخرین اقدام</th><th>پیشنهاد فعال</th><th>سوابق</th></tr> : viewMode === 'missions' ? <tr><th>شناسه مأموریت</th><th>مشتری</th><th>درخواست</th><th>وضعیت</th><th>موعد تحویل</th><th>زمان ایجاد</th><th>سوابق</th></tr> : viewMode === 'presence' ? <tr><th>شناسهٔ پیک</th><th>وضعیت حضور</th><th>زون</th><th>آخرین تغییر حضور</th><th>مشخصات</th></tr> : viewMode === 'couriers' ? <tr><th>شناسه پیک</th><th>نوع عملیات</th><th>وضعیت اعتماد</th><th>زون‌های ترجیحی</th><th>زون‌های تأییدشده</th><th>زون اصلی</th><th>آخرین تغییر</th><th>جزئیات</th></tr> : viewMode === 'finance' ? <tr><th>مأموریت</th><th>وضعیت</th><th>نرخ ثبت‌شده</th><th>مسافت</th><th>کرایه</th><th>سهم خریدار</th><th>سهم فروشنده</th><th>کمیسیون مونو (برآورد)</th><th>مالیات کمیسیون</th><th>خالص پیک (برآورد)</th></tr> : viewMode === 'issues' ? <tr><th>شناسهٔ گزارش</th><th>مأموریت</th><th>پیک</th><th>نوع مشکل</th><th>وضعیت</th><th>گزارش‌شده</th><th>آخرین تغییر</th><th>سوابق مأموریت</th></tr> : viewMode === 'sla' ? <tr><th>شناسهٔ مأموریت</th><th>مشتری</th><th>وضعیت مأموریت</th><th>موعد تحویل</th><th>پیک پذیرفته‌شده</th><th>وضعیت SLA</th><th>سوابق</th></tr> : <tr><th>شناسه پنجره</th><th>شناسه پیک</th><th>زون</th><th>شروع</th><th>پایان</th><th>وضعیت</th><th>ثبت‌کننده</th><th>زمان ثبت</th><th>عملیات</th></tr>}</thead>
              <tbody>
                {loading && <tr><td colSpan={viewMode === 'finance' ? 10 : viewMode === 'zones' ? 9 : viewMode === 'sla' ? 7 : viewMode === 'presence' ? 5 : viewMode === 'issues' ? 8 : 8} className="state-cell"><span className="spinner" />در حال دریافت اطلاعات…</td></tr>}
                {!loading && (viewMode === 'dispatch' ? items.length === 0 : viewMode === 'missions' ? missionItems.length === 0 : viewMode === 'issues' ? issueItems.length === 0 : viewMode === 'presence' ? presenceItems.length === 0 : viewMode === 'couriers' ? courierItems.length === 0 : viewMode === 'finance' ? financeItems.length === 0 : viewMode === 'sla' ? slaItems.length === 0 : !availabilityPage?.items.length) && <tr><td colSpan={viewMode === 'finance' ? 10 : viewMode === 'sla' ? 7 : viewMode === 'presence' ? 5 : 8} className="state-cell"><span className="empty-icon">⌕</span><strong>{token ? viewMode === 'zones' && !availabilityPage ? 'زون و بازه را وارد و محاسبه کنید' : 'رکوردی در این صفحه پیدا نشد' : 'برای شروع، به API متصل شوید'}</strong><small>{token ? 'صفحه را تازه کنید یا به صفحهٔ قبل برگردید.' : 'هیچ دادهٔ نمونه‌ای در این صفحه نمایش داده نمی‌شود.'}</small></td></tr>}
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
                {!loading && viewMode === 'sla' && slaItems.map((alert) => <tr key={alert.mission_id}>
                  <td><strong className="mission-id" dir="ltr">{alert.mission_id}</strong></td>
                  <td dir="ltr">{alert.customer_id}</td>
                  <td><span className="badge neutral">{alert.status}</span></td>
                  <td>{formatDate(alert.committed_delivery_at)}</td>
                  <td dir="ltr">{alert.courier_id || <span className="cell-secondary">تخصیص پذیرفته‌شده ندارد</span>}</td>
                  <td><span className={`badge ${alert.is_overdue ? 'danger' : 'warning'}`}>{alert.is_overdue ? 'از موعد گذشته' : new Date(alert.committed_delivery_at) <= new Date(slaEvaluatedAt || '') ? 'موعد رسیده' : 'نزدیک موعد'}</span></td>
                  <td><button className="history-button" onClick={() => void showMissionDetails(alert.mission_id)}>جزئیات</button></td>
                </tr>)}
                {!loading && viewMode === 'issues' && issueItems.map((issue) => <tr key={issue.report_id}>
                  <td><strong className="mission-id" dir="ltr">{issue.report_id}</strong></td>
                  <td><span className="cell-primary mission-id" dir="ltr">{issue.mission_id}</span></td>
                  <td dir="ltr">{issue.courier_id}</td><td>{issueCodeLabel(issue.issue_code)}</td>
                  <td><span className={`badge ${issue.status === 'OPEN' ? 'warning' : 'success'}`}>{issue.status === 'OPEN' ? 'باز' : 'رسیدگی‌شده'}</span></td>
                  <td>{formatDate(issue.reported_at)}</td><td>{formatDate(issue.updated_at)}</td>
                  <td><button className="history-button" onClick={() => void showMissionDetails(issue.mission_id)}>جزئیات مأموریت</button></td>
                </tr>)}
                {!loading && viewMode === 'presence' && presenceItems.map((courier) => <tr key={courier.courier_id}>
                  <td><strong className="mission-id" dir="ltr">{courier.courier_id}</strong></td>
                  <td><span className={`badge ${courier.status === 'ONLINE' ? 'success' : 'neutral'}`}>{courier.status === 'ONLINE' ? 'آنلاین' : 'آفلاین'}</span></td>
                  <td dir="ltr">{courier.zone_id || '—'}</td><td>{formatDate(courier.updated_at)}</td>
                  <td><button className="history-button" onClick={() => void showCourierDetails(courier.courier_id)}>جزئیات پیک</button></td>
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
          <footer className="table-footer"><span>{fa.format(viewMode === 'dispatch' ? items.length : viewMode === 'missions' ? missionItems.length : viewMode === 'couriers' ? courierItems.length : viewMode === 'presence' ? presenceItems.length : viewMode === 'issues' ? issueItems.length : viewMode === 'finance' ? financeItems.length : viewMode === 'sla' ? slaItems.length : availabilityPage?.items.length || 0)} ردیف در این صفحه</span><div className="pagination"><button onClick={goPrevious} disabled={!token || loading || pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(pageIndex + 1)}</span><button onClick={goNext} disabled={!token || loading || !(viewMode === 'dispatch' ? nextCursor : viewMode === 'missions' ? missionNextCursor : viewMode === 'couriers' ? courierNextCursor : viewMode === 'finance' ? financeNextCursor : viewMode === 'sla' ? slaNextCursor : viewMode === 'presence' ? presenceNextCursor : availabilityNextCursor)}>بعدی</button></div></footer>
        </section>}
        {viewMode === 'dispatch' && <p className="data-note">آمار بالا فقط از ردیف‌های صفحهٔ جاری محاسبه شده است؛ API فعلاً شمار کل صف را برنمی‌گرداند.</p>}
        {viewMode === 'issues' && <p className="data-note">این صندوق فقط فرادادهٔ ساختاریافتهٔ گزارش را نشان می‌دهد؛ متن آزاد یا موقعیت مکانی پیک در این API ارائه نمی‌شود. این نما گزارش را resolve یا وضعیت مأموریت را تغییر نمی‌دهد.</p>}
        {viewMode === 'presence' && <p className="data-note">آمار فقط از ردیف‌های صفحهٔ جاری محاسبه شده و وضعیت براساس snapshot API است؛ این نما polling خودکار یا موقعیت GPS ارائه نمی‌کند.</p>}
        {viewMode === 'sla' && <p className="data-note">خلاصه فقط از ردیف‌های همین صفحه است. هشدارها در زمان «مبنای ارزیابی» محاسبه شده‌اند؛ برای تازه‌سازی، دکمهٔ ↻ را بزنید.</p>}
        {viewMode === 'workers' && <p className="data-note">این وضعیت درون‌فرایندی و مخصوص همین نمونهٔ API است؛ در استقرار چندنمونه‌ای، وضعیت سایر نمونه‌ها را پوشش نمی‌دهد و health کلی API محسوب نمی‌شود. دریافت خودکار انجام نمی‌شود.</p>}
        {viewMode === 'zones' && <p className="data-note">این داده‌ها پنجره‌های کاری ثبت‌شده‌اند؛ حضور زنده، ظرفیت لحظه‌ای یا اختیار تخصیص از آن‌ها نتیجه نمی‌شود.</p>}{viewMode === 'finance' && <p className="data-note">این نما فقط از snapshot قطعی تعرفه و مفروضات تجاری Phase 1 برآورد می‌سازد؛ پرداخت، موجودی، برداشت، تسویهٔ واقعی یا وصول از حنا را گزارش نمی‌کند.</p>}
      </main>

      {webhookAction && <div className="modal-backdrop" role="presentation" onClick={() => !webhookActionLoading && setWebhookAction(null)}><section className="timeline-modal webhook-confirm" role="dialog" aria-modal="true" aria-labelledby="webhook-confirm-title" onClick={(event) => event.stopPropagation()}>
        <header><div><span className="eyebrow">عملیات وب‌هوک</span><h2 id="webhook-confirm-title">{webhookAction.kind === 'replay' ? 'ارسال دوبارهٔ تحویل' : 'لغو تحویل در صف'}</h2></div><button className="icon-button" onClick={() => !webhookActionLoading && setWebhookAction(null)} aria-label="بستن">×</button></header>
        <p className="modal-state" dir="ltr">{webhookAction.deliveryId}</p>
        <p>{webhookAction.kind === 'replay' ? 'درخواست replay به API ارسال می‌شود و ممکن است endpoint مقصد را دوباره فراخوانی کند. فقط گزارش‌های dead-letter قابل replay هستند و API سقف و فعال‌بودن endpoint را بررسی می‌کند.' : 'API فقط تحویل‌های در صف یا retrying بدون lease فعال را لغو می‌کند. ممکن است درخواست به دلیل شروع ارسال هم‌زمان رد شود.'}</p>
        {webhookActionError && <div className="webhook-inline-error" role="alert">{webhookActionError}<small>در صورت نامشخص‌بودن نتیجه، دکمهٔ تلاش مجدد همان کلید idempotency را حفظ می‌کند.</small></div>}
        <div className="capacity-actions"><button className="primary-button" onClick={() => void performWebhookAction()} disabled={webhookActionLoading}>{webhookActionLoading ? 'در حال ارسال…' : webhookActionError ? 'تلاش مجدد' : 'تأیید عملیات'}</button><button className="secondary-button" onClick={() => setWebhookAction(null)} disabled={webhookActionLoading}>انصراف</button></div>
      </section></div>}
      {timelineTitle && <div className="modal-backdrop" role="presentation" onClick={closeDetails}><section className="timeline-modal detail-modal" role="dialog" aria-modal="true" aria-labelledby="timeline-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">جزئیات و سوابق عملیاتی</span><h2 id="timeline-title" dir="ltr">{timelineTitle}</h2></div><button className="icon-button" onClick={closeDetails} aria-label="بستن">×</button></header>{timelineLoading ? <p className="modal-state">در حال دریافت اطلاعات…</p> : viewMode === 'couriers' || viewMode === 'presence' ? <>
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
