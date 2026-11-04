export type DashboardSummaryMetrics = {
  as_of: string
  business_date: string
  sla_horizon_minutes: number
  active_mission_count: number
  unassigned_mission_count: number
  online_courier_count: number
  sla_at_risk_count: number
  sla_overdue_count: number
  delivered_today_count: number
  on_time_delivered_today_count: number
  on_time_delivery_rate_percent: number | null
}

const fa = new Intl.NumberFormat('fa-IR')

export default function DashboardSummaryCards({
  summary,
  onOpenPresence,
  onOpenDispatch,
  onOpenSla,
  onOpenMissions,
  actionsDisabled = false,
}: {
  summary: DashboardSummaryMetrics | null
  onOpenPresence: () => void
  onOpenDispatch: () => void
  onOpenSla: () => void
  onOpenMissions: () => void
  actionsDisabled?: boolean
}) {
  const updatedAt = summary ? new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(summary.as_of)) : null
  return (
    <section className="kpi-grid dashboard-summary-strip" aria-label="خلاصهٔ صفحهٔ جاری">
      <article className="kpi-card"><div><small>تحویل به‌موقع امروز</small><strong>{summary?.on_time_delivery_rate_percent == null ? '—' : `${fa.format(summary.on_time_delivery_rate_percent)}٪`}</strong><span className="sr-only">{summary ? `${fa.format(summary.on_time_delivered_today_count)} از ${fa.format(summary.delivered_today_count)} · ${summary.business_date}` : 'در انتظار دادهٔ API'}</span></div></article>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenPresence} disabled={actionsDisabled} aria-label="مشاهدهٔ فهرست وضعیت پیک‌ها">
        <span className="kpi-card-copy"><small>پیک‌های آنلاین</small><strong>{summary ? fa.format(summary.online_courier_count) : '—'}</strong><span className="sr-only">حضور زندهٔ پیک فعال</span></span>
      </button>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenDispatch} disabled={actionsDisabled} aria-label="رفتن به صف مأموریت‌های بدون تخصیص">
        <span className="kpi-card-copy"><small>بدون تخصیص</small><strong>{summary ? fa.format(summary.unassigned_mission_count) : '—'}</strong><span className="sr-only">بدون پیشنهاد پذیرفته‌شده</span></span>
      </button>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenSla} disabled={actionsDisabled} aria-label="مشاهدهٔ مأموریت‌های دارای هشدار SLA">
        <span className="kpi-card-copy"><small>در معرض نقض استاندارد</small><strong>{summary ? fa.format(summary.sla_at_risk_count) : '—'}</strong><span className="sr-only">{summary ? `افق ${fa.format(summary.sla_horizon_minutes)} دقیقه · گذشته از موعد ${fa.format(summary.sla_overdue_count)}` : 'در انتظار دادهٔ API'}</span></span>
      </button>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenMissions} disabled={actionsDisabled} aria-label="مشاهدهٔ فهرست مأموریت‌ها">
        <span className="kpi-card-copy"><small>مأموریت‌های فعال</small><strong>{summary ? fa.format(summary.active_mission_count) : '—'}</strong><span className="sr-only">{updatedAt ? `به‌روزرسانی ${updatedAt}` : 'در انتظار دادهٔ API'}</span></span>
      </button>
    </section>
  )
}
