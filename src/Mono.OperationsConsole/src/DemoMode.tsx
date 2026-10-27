import { useState } from 'react'

type DemoSection = 'dispatch' | 'missions' | 'couriers' | 'presence' | 'sla' | 'issues'

type DemoMission = {
  id: string
  customer: string
  branch: string
  status: string
  courier: string
  deadline: string
  risk: 'عادی' | 'نزدیک موعد' | 'از موعد گذشته'
}

const missions: DemoMission[] = [
  { id: 'DEMO-MSN-1001', customer: 'مشتری نمایشی ۰۱', branch: 'شعبه نمایشی ونک', status: 'در انتظار پیک', courier: '—', deadline: 'امروز، ۱۲:۳۰', risk: 'عادی' },
  { id: 'DEMO-MSN-1002', customer: 'مشتری نمایشی ۰۲', branch: 'شعبه نمایشی یوسف‌آباد', status: 'پیک پذیرفته', courier: 'DEMO-CR-021', deadline: 'امروز، ۱۲:۱۵', risk: 'نزدیک موعد' },
  { id: 'DEMO-MSN-1003', customer: 'مشتری نمایشی ۰۳', branch: 'شعبه نمایشی ونک', status: 'در مسیر تحویل', courier: 'DEMO-CR-008', deadline: 'امروز، ۱۲:۴۵', risk: 'عادی' },
  { id: 'DEMO-MSN-1004', customer: 'مشتری نمایشی ۰۴', branch: 'شعبه نمایشی صادقیه', status: 'در انتظار پیک', courier: '—', deadline: 'امروز، ۱۲:۰۵', risk: 'از موعد گذشته' },
  { id: 'DEMO-MSN-1005', customer: 'مشتری نمایشی ۰۵', branch: 'شعبه نمایشی صادقیه', status: 'تحویل‌شده', courier: 'DEMO-CR-013', deadline: 'امروز، ۱۱:۵۰', risk: 'عادی' },
  { id: 'DEMO-MSN-1006', customer: 'مشتری نمایشی ۰۶', branch: 'شعبه نمایشی جردن', status: 'پیک پذیرفته', courier: 'DEMO-CR-021', deadline: 'امروز، ۱۲:۲۰', risk: 'نزدیک موعد' },
]

const couriers = [
  { id: 'DEMO-CR-008', mode: 'موتورسیکلت', status: 'آنلاین', zone: 'زون نمایشی شمال' },
  { id: 'DEMO-CR-013', mode: 'موتورسیکلت', status: 'آنلاین', zone: 'زون نمایشی غرب' },
  { id: 'DEMO-CR-021', mode: 'موتورسیکلت', status: 'مشغول', zone: 'زون نمایشی مرکز' },
  { id: 'DEMO-CR-034', mode: 'موتورسیکلت', status: 'آفلاین', zone: 'زون نمایشی شمال' },
]

const titles: Record<DemoSection, string> = {
  dispatch: 'صف مأموریت‌های در انتظار تخصیص پیک',
  missions: 'فهرست مأموریت‌های نمایشی',
  couriers: 'فهرست پیک‌های نمایشی',
  presence: 'وضعیت حضور پیک‌های نمایشی',
  sla: 'هشدارهای موعد تحویل نمایشی',
  issues: 'گزارش‌های نمایشی پیک‌ها',
}

export default function DemoMode() {
  const [section, setSection] = useState<DemoSection>('dispatch')
  const currentMissions = section === 'sla'
    ? missions.filter((mission) => mission.risk !== 'عادی')
    : section === 'dispatch'
      ? missions.filter((mission) => mission.status === 'در انتظار پیک' || mission.status === 'پیک پذیرفته')
      : missions
  const showingCouriers = section === 'couriers' || section === 'presence'

  return (
    <div className="app-shell demo-shell" dir="rtl">
      <aside className="sidebar">
        <div className="brand-lockup"><span className="brand-mark">م</span><span><strong>مونو</strong><small>LOGISTICS</small></span></div>
        <div className="side-caption">فضای عملیات</div>
        <nav className="navigation" aria-label="ناوبری نمایشی">
          <button className={`nav-item ${section === 'dispatch' ? 'active' : ''}`} onClick={() => setSection('dispatch')}><span className="nav-icon">▦</span>صف dispatch</button>
          <button className={`nav-item ${section === 'sla' ? 'active' : ''}`} onClick={() => setSection('sla')}><span className="nav-icon">!</span>هشدارهای SLA</button>
          <button className={`nav-item ${section === 'missions' ? 'active' : ''}`} onClick={() => setSection('missions')}><span className="nav-icon">↗</span>همهٔ مأموریت‌ها</button>
          <button className={`nav-item ${section === 'couriers' ? 'active' : ''}`} onClick={() => setSection('couriers')}><span className="nav-icon">⌁</span>پیک‌ها</button>
          <button className={`nav-item ${section === 'presence' ? 'active' : ''}`} onClick={() => setSection('presence')}><span className="nav-icon">●</span>وضعیت حضور پیک‌ها</button>
          <button className={`nav-item ${section === 'issues' ? 'active' : ''}`} onClick={() => setSection('issues')}><span className="nav-icon">!</span>صندوق مشکلات پیک‌ها</button>
        </nav>
        <div className="sidebar-footer"><span className="online-dot" />پیش‌نمایش نمایشی</div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div className="topbar-actions"><span className="demo-pill">فقط نمایش · داده ساختگی</span></div>
          <div className="page-heading"><h1>مرکز کنترل عملیات</h1><p>{titles[section]}</p></div>
        </header>

        <section className="demo-banner" role="note">
          <strong>حالت نمایشی</strong>
          <span>همهٔ نام‌ها و مأموریت‌ها ساختگی‌اند. این صفحه به API وصل نیست و هیچ عملیاتی ثبت یا تغییر نمی‌دهد.</span>
        </section>

        <section className="kpi-grid" aria-label="شاخص‌های نمایشی">
          <article className="kpi-card"><span className="kpi-icon cyan">▦</span><div><small>مأموریت‌های فعال نمایشی</small><strong>۱۲</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon blue">●</span><div><small>پیک‌های آنلاین نمایشی</small><strong>۸</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon amber">◷</span><div><small>نزدیک موعد تحویل</small><strong>۲</strong></div></article>
          <article className="kpi-card"><span className="kpi-icon red">!</span><div><small>بدون پیک پذیرفته‌شده</small><strong>۳</strong></div></article>
        </section>

        <section className="queue-panel">
          <div className="panel-heading"><div><span className="eyebrow">دادهٔ نمونه</span><h2>{titles[section]}</h2></div><span className="panel-meta">به‌روزرسانی نمایشی · همین حالا</span></div>
          <div className="table-wrap">
            <table className="demo-table">
              <thead><tr>{showingCouriers
                ? <><th>شناسه پیک</th><th>وسیله</th><th>وضعیت</th><th>زون</th></>
                : section === 'issues'
                  ? <><th>شناسه گزارش</th><th>مأموریت نمایشی</th><th>نوع گزارش</th><th>وضعیت</th><th>زمان ثبت</th></>
                  : <><th>شناسه مأموریت</th><th>مشتری / شعبه</th><th>وضعیت</th><th>پیک نمایشی</th><th>موعد تحویل</th><th>وضعیت موعد</th></>}</tr></thead>
              <tbody>
                {showingCouriers ? couriers.map((courier) => <tr key={courier.id}><td><strong className="mission-id" dir="ltr">{courier.id}</strong></td><td>{courier.mode}</td><td><span className={`badge ${courier.status === 'آنلاین' ? 'success' : 'neutral'}`}>{courier.status}</span></td><td>{courier.zone}</td></tr>)
                  : section === 'issues' ? [
                    ['DEMO-ISS-001', 'DEMO-MSN-1002', 'گیرنده در دسترس نیست', 'باز', 'امروز، ۱۱:۵۸'],
                    ['DEMO-ISS-002', 'DEMO-MSN-1004', 'مسیر مسدود', 'رسیدگی‌شده', 'امروز، ۱۱:۴۲'],
                  ].map((issue) => <tr key={issue[0]}>{issue.map((value, index) => <td key={index} dir={index < 2 ? 'ltr' : undefined}>{value}</td>)}</tr>)
                    : currentMissions.map((mission) => <tr key={mission.id}>
                      <td><strong className="mission-id" dir="ltr">{mission.id}</strong></td>
                      <td><span className="cell-primary">{mission.customer}</span><span className="cell-secondary">{mission.branch}</span></td>
                      <td><span className="badge neutral">{mission.status}</span></td>
                      <td dir="ltr">{mission.courier}</td><td>{mission.deadline}</td>
                      <td><span className={`badge ${mission.risk === 'از موعد گذشته' ? 'danger' : mission.risk === 'نزدیک موعد' ? 'warning' : 'success'}`}>{mission.risk}</span></td>
                    </tr>)}
              </tbody>
            </table>
          </div>
          <footer className="table-footer"><span>{section === 'issues' ? '۲' : showingCouriers ? '۴' : String(currentMissions.length)} ردیف نمایشی</span><span className="demo-footer-note">فقط دادهٔ نمایشی · غیرواقعی</span></footer>
        </section>
      </main>
    </div>
  )
}
