import { useMemo, useState } from 'react'

type Mission = {
  id: string
  state: 'active' | 'complete' | 'issue'
  stateLabel: string
  route: string
  started: string
  distance: string
  amount: string
  outcome: string
}

const missions: Mission[] = [
  { id: 'MIS-۱۰۵۰۳', state: 'active', stateLabel: 'فعال', route: 'حنا ونک → سارا نادری', started: 'شروع ۱۲:۱۸', distance: '۳٫۱ کیلومتر', amount: '۳۱٬۱۵۰ تومان', outcome: 'در مسیر مقصد' },
  { id: 'MIS-۱۰۴۹۸', state: 'complete', stateLabel: 'تکمیل', route: 'حنا مرکزی → علی رضایی', started: 'امروز ۱۱:۰۶', distance: '۴٫۲ کیلومتر', amount: '۲۴٬۹۲۰ تومان', outcome: 'تحویل موفق' },
  { id: 'MIS-۱۰۴۹۱', state: 'complete', stateLabel: 'تکمیل', route: 'حنا جردن → مریم مرادی', started: 'امروز ۱۰:۱۲', distance: '۲٫۹ کیلومتر', amount: '۲۲٬۲۵۰ تومان', outcome: 'تحویل موفق' },
  { id: 'MIS-۱۰۴۸۵', state: 'complete', stateLabel: 'تکمیل', route: 'حنا میرداماد → آرش کاظمی', started: 'امروز ۰۹:۲۸', distance: '۳٫۶ کیلومتر', amount: '۲۶٬۷۰۰ تومان', outcome: 'تحویل موفق' },
]

const filters = [
  { id: 'all', label: 'همه' },
  { id: 'active', label: 'فعال' },
  { id: 'complete', label: 'تکمیل‌شده' },
  { id: 'issue', label: 'مسئله‌دار' },
] as const

type Filter = typeof filters[number]['id']

export default function MissionHistory() {
  const [filter, setFilter] = useState<Filter>('all')
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<Mission | null>(null)
  const visibleMissions = useMemo(() => missions.filter(mission => {
    const matchesFilter = filter === 'all' || mission.state === filter
    const searchable = `${mission.id} ${mission.route} ${mission.outcome}`.toLocaleLowerCase()
    return matchesFilter && searchable.includes(query.trim().toLocaleLowerCase())
  }), [filter, query])

  return <section className="content-scroll missions-history-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/avatar.svg" alt="" />
      <div className="profile-copy"><strong>مأموریت‌ها</strong><small>فعال و تاریخچه</small></div>
    </header>

    <section className="missions-summary-card">
      <div className="missions-summary-metrics">
        <div><small>امروز</small><strong>۸</strong></div>
        <div><small>تکمیل‌شده</small><strong className="complete-value">۷</strong></div>
        <div><small>فعال</small><strong className="active-value">۱</strong></div>
        <div><small>خالص امروز</small><strong className="complete-value">۲۶۷٬۰۰۰</strong></div>
      </div>
    </section>

    <div className="mission-history-filters" role="tablist" aria-label="فیلتر مأموریت‌ها">
      {filters.map(item => <button key={item.id} role="tab" aria-selected={filter === item.id} className={filter === item.id ? 'selected' : ''} onClick={() => setFilter(item.id)}>{item.label}</button>)}
    </div>

    <label className="mission-history-search">
      <span className="sr-only">جست‌وجوی شناسه یا مقصد</span>
      <input value={query} onChange={event => setQuery(event.target.value)} placeholder="جست‌وجوی شناسه یا مقصد" />
    </label>

    <div className="mission-history-list" aria-live="polite">
      {visibleMissions.length ? visibleMissions.map(mission => <button className={`mission-history-card ${mission.state === 'active' ? 'is-active' : ''}`} key={mission.id} onClick={() => setSelected(mission)}>
        <span className="mission-history-head"><span className={`mission-history-state ${mission.state}`}>{mission.stateLabel}</span><strong className="mission-history-id">{mission.id}</strong></span>
        <strong className="mission-history-route">{mission.route}</strong>
        <small className="mission-history-meta">{mission.started} • {mission.distance}</small>
        <span className="mission-history-foot"><strong>{mission.amount}</strong><small>{mission.outcome}</small></span>
      </button>) : <div className="mission-history-empty">مأموریتی با این مشخصات پیدا نشد.</div>}
    </div>

    {selected && <div className="mission-detail-backdrop" role="presentation" onClick={() => setSelected(null)}>
      <section className="mission-detail-dialog" role="dialog" aria-modal="true" aria-labelledby="history-detail-title" onClick={event => event.stopPropagation()}>
        <button className="mission-detail-close" aria-label="بستن جزئیات" onClick={() => setSelected(null)}>×</button>
        <h2 id="history-detail-title">جزئیات مأموریت {selected.id}</h2>
        <p>{selected.route}</p>
        <p>{selected.started} • {selected.distance}</p>
        <p>{selected.outcome} • درآمد خالص {selected.amount}</p>
        <button className="mission-detail-done" onClick={() => setSelected(null)}>بستن جزئیات</button>
      </section>
    </div>}
  </section>
}
