Warning: truncated output (original token count: 31313)
Total output lines: 1379

import { useCallback, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import PolicyWorkbench from './PolicyWorkbench'
import OnboardingWorkbench from './OnboardingWorkbench'
import AdmissionsWorkbench from './AdmissionsWorkbench'
import DashboardSummaryCards from './DashboardSummaryCards'
import CourierPlanningEvidence from './CourierPlanningEvidence'
import CourierControlWorkbench from './CourierControlWorkbench'
import RoutePlanWorkbench from './RoutePlanWorkbench'
import AuditWorkbench from './AuditWorkbench'
import ApiKeyWorkbench from './ApiKeyWorkbench'
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
  const [viewMode, setViewMode] = useState<'dispatch' | 'missions' | 'routes' | 'audit' | 'couriers' | 'zones' | 'finance' | 'sla' | 'presence' | 'issues' | 'workers' | 'webhooks' | 'policies' | 'onboarding' | 'admissions'>('dispatch')
  const [policyReloadKey, setPolicyReloadKey] = useState(0)
  const [routeReloadKey, setRouteReloadKey] = useState(0)
  const [auditReloadKey, setAuditReloadKey] = useState(0)
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
  const [webhookTab, setWebhookTab] = useState<'deliveries' | 'audit' | 'credentials'>('deliveries')
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
  const [selectedCourierId, setSelectedCourierId] = useState('')
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
      setError(cause instanceof Error ? cause.message : 'ارتباط با API…21313 tokens truncated…mission.latestAttempt?.outcome || 'بدون تلاش ثبت‌شده'}</span></td>
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
          </section>
        </div>}
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
      {timelineTitle && <div className={`modal-backdrop${viewMode !== 'couriers' && viewMode !== 'presence' ? ' mission-drawer-backdrop' : ''}`} role="presentation" onClick={closeDetails}><section className={`timeline-modal detail-modal${viewMode !== 'couriers' && viewMode !== 'presence' ? ' mission-detail-drawer' : ''}`} role="dialog" aria-modal="true" aria-labelledby="timeline-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">{viewMode !== 'couriers' && viewMode !== 'presence' ? 'جزئیات مأموریت' : 'جزئیات و سوابق عملیاتی'}</span><h2 id="timeline-title" dir="ltr">{timelineTitle}</h2></div><button className="icon-button" onClick={closeDetails} aria-label="بستن">×</button></header>{timelineLoading ? <p className="modal-state">در حال دریافت اطلاعات…</p> : viewMode === 'couriers' || viewMode === 'presence' ? <>
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
        <section className="detail-section"><h3>وضعیت و تعهد</h3>{missionDetail ? <div className="drawer-info-list">
          <div><strong>{formatDate(missionDetail.committedDeliveryAt)}</strong><span>زمان تعهدشده</span></div>
          <div><strong className="unavailable-value">از API دریافت نمی‌شود</strong><span>زمان تخمینی فعلی</span></div>
          <div><strong><span className="badge neutral">{missionDetail.status}</span></strong><span>وضعیت مأموریت</span></div>
          <div><strong className="unavailable-value">قابل محاسبه نیست</strong><span>زمان باقیمانده</span></div>
        </div> : <p className="modal-state">جزئیات مأموریت در دسترس نیست.</p>}</section>
        <section className="detail-section"><h3>اجرا و مسیر</h3>{missionDetail ? <div className="drawer-info-list">
          <div><strong className="unavailable-value">از API دریافت نمی‌شود</strong><span>پیک تخصیص‌یافته</span></div>
          <div><strong className="unavailable-value">از API دریافت نمی‌شود</strong><span>شیوهٔ حمل</span></div>
          <div><strong dir="ltr">{missionDetail.deliveryRequestId}</strong><span>درخواست تحویل</span></div>
          <div><strong className="unavailable-value">از API دریافت نمی‌شود</strong><span>مبدأ و مقصد</span></div>
          <div><strong dir="ltr">{missionDetail.customerId}</strong><span>شناسه مشتری</span></div>
        </div> : <p className="modal-state">اطلاعات اجرا در دسترس نیست.</p>}</section>
        <section className="detail-section decision-evidence"><h3>دلیل تصمیم سیستم</h3><p>دادهٔ دلیل تخصیص در API فعلی موجود نیست.</p></section>
        <section className="detail-section"><h3>تاریخچه مأموریت</h3>{missionTimeline.length ? <ol className="drawer-timeline">{[...missionTimeline].sort((a,b) => a.sequence-b.sequence).map((event) => <li key={event.eventId}><time>{formatDate(event.occurredAt)}</time><span className="event-dot"/><div><strong>{event.eventType}</strong><small>رویداد {fa.format(event.sequence)}</small></div></li>)}</ol> : <p className="modal-state">رویدادی در timeline ثبت نشده است.</p>}</section>
        <section className="detail-section"><h3>سوابق دسترسی عملیات</h3>{missionAudit.length ? <div className="audit-list">{missionAudit.map((record) => <article key={record.auditId}><div><strong>{record.action}</strong><small>{record.actorId}</small></div><span>{formatDate(record.occurredAt)}</span><em>{fa.format(record.resultCount)} نتیجه</em></article>)}</div> : <p className="modal-state">سابقه‌ای برای نمایش برنگشت.</p>}</section>
        <footer className="drawer-actions"><div className="drawer-action-row"><button disabled title="API فعلی مسیر فعال مأموریت را ارائه نمی‌کند">مشاهده مسیر</button><button disabled title="اطلاعات تماس پیک در API مأموریت موجود نیست">تماس با پیک</button></div><div className="drawer-action-row"><button disabled title="API عملیات ثبت استثنا را ارائه نمی‌کند">ثبت استثنا</button><button disabled title="API عملیات تخصیص مجدد را ارائه نمی‌کند">تخصیص مجدد</button></div><button className="drawer-cancel" disabled title="لغو مأموریت از این پنل در API پشتیبانی نمی‌شود">لغو مأموریت</button><small>اقدام‌های عملیاتی تا زمان پشتیبانی API غیرفعال هستند.</small></footer>
      </>}</section></div>}
    </div>
  )
}
