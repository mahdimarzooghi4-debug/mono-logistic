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
}: {
  summary: DashboardSummaryMetrics | null
  onOpenPresence: () => void
  onOpenDispatch: () => void
  onOpenSla: () => void
  onOpenMissions: () => void
}) {
  const updatedAt = summary ? new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(summary.as_of)) : null
  return (
    <section className="kpi-grid" aria-label="خلاصهٔ صفحهٔ جاری">
      <article className="kpi-card"><span className="kpi-icon cyan">%</span><div><small>تحویل به‌موقع امروز</small><strong>{summary?.on_time_delivery_rate_percent == null ? '—' : `${fa.format(summary.on_time_delivery_rate_percent)}٪`}</strong><small>{summary ? `${fa.format(summary.on_time_delivered_today_count)} از ${fa.format(summary.delivered_today_count)} · ${summary.business_date}` : 'در انتظار دادهٔ API'}</small></div></article>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenPresence} aria-label="مشاهدهٔ فهرست وضعیت پیک‌ها">
        <span className="kpi-icon blue">●</span><span className="kpi-card-copy"><small>پیک‌های آنلاین</small><strong>{summary ? fa.format(summary.online_courier_count) : '—'}</strong><small>حضور زندهٔ پیک فعال</small><small className="kpi-action-hint">مشاهدهٔ وضعیت پیک‌ها ←</small></span>
      </button>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenDispatch} aria-label="رفتن به صف مأموریت‌های بدون تخصیص">
        <span className="kpi-icon amber">◷</span><span className="kpi-card-copy"><small>بدون تخصیص پیک</small><strong>{summary ? fa.format(summary.unassigned_mission_count) : '—'}</strong><small>بدون پیشنهاد پذیرفته‌شده</small><small className="kpi-action-hint">رفتن به صف مأموریت ←</small></span>
      </button>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenSla} aria-label="مشاهدهٔ مأموریت‌های دارای هشدار SLA">
        <span className="kpi-icon red">!</span><span className="kpi-card-copy"><small>در معرض نقض SLA</small><strong>{summary ? fa.format(summary.sla_at_risk_count) : '—'}</strong><small>{summary ? `افق ${fa.format(summary.sla_horizon_minutes)} دقیقه · گذشته از موعد ${fa.format(summary.sla_overdue_count)}` : 'در انتظار دادهٔ API'}</small><small className="kpi-action-hint">مشاهدهٔ هشدارهای SLA ←</small></span>
      </button>
      <button type="button" className="kpi-card kpi-card-action" onClick={onOpenMissions} aria-label="مشاهدهٔ فهرست مأموریت‌ها">
        <span className="kpi-icon cyan">▦</span><span className="kpi-card-copy"><small>مأموریت‌های فعال</small><strong>{summary ? fa.format(summary.active_mission_count) : '—'}</strong><small>{updatedAt ? `به‌روزرسانی ${updatedAt}` : 'در انتظار دادهٔ API'}</small><small className="kpi-action-hint">رفتن به فهرست مأموریت‌ها ←</small></span>
      </button>
    </section>
  )
}
