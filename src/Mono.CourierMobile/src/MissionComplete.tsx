import { useState } from 'react'

type MissionCompleteProps = { onHome: () => void; onNotice: (message: string) => void }

export default function MissionComplete({ onHome, onNotice }: MissionCompleteProps) {
  const [detailsOpen, setDetailsOpen] = useState(false)

  return <section className="content-scroll complete-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/avatar.svg" alt="" />
      <div className="profile-copy"><strong>مأموریت تکمیل شد</strong><small>MIS-۱۰۴۹۸ • تحویل موفق</small></div>
    </header>

    <section className="complete-hero">
      <img src="/figma/mission-success.svg" alt="" />
      <h1>تحویل با موفقیت ثبت شد</h1>
      <p>درآمد خالص این مأموریت به موجودی قابل برداشت اضافه شد.</p>
    </section>

    <section className="complete-summary-card">
      <div className="complete-card-heading"><span className="complete-pill">تکمیل‌شده</span><h2>خلاصه مأموریت</h2></div>
      <div className="complete-summary-metrics">
        <div><small>زمان کل</small><strong>۳۱ دقیقه</strong></div>
        <div><small>مسافت</small><strong>۴٫۲ کیلومتر</strong></div>
        <div><small>تحویل</small><strong className="success-text">موفق</strong></div>
      </div>
    </section>

    <section className="earnings-breakdown-card">
      <h2>تسویه این مأموریت</h2>
      <div className="earning-row"><strong>۲۸٬۰۰۰ تومان</strong><span>کرایه مأموریت</span></div>
      <div className="earning-row deduction"><strong>۲٬۸۰۰ تومان</strong><span>کمیسیون مونو • ۱۰٪</span></div>
      <div className="earning-row deduction"><strong>۲۸۰ تومان</strong><span>مالیات کمیسیون • ۱۰٪</span></div>
      <div className="earnings-divider" />
      <div className="earning-row net"><strong>۲۴٬۹۲۰ تومان</strong><span>خالص پیک • ۸۹٪</span></div>
      <div className="wallet-update"><span>قابل برداشت</span><strong>۵۱۱٬۴۲۰ تومان</strong></div>
    </section>

    <section className="next-mission-card">
      <div className="complete-card-heading"><span className="online-pill">آنلاین</span><h2>آماده مأموریت بعدی</h2></div>
      <p>منطقه فعال: مرکزی • ظرفیت فعلی شما آزاد است.</p>
    </section>

    <div className="complete-actions">
      <button onClick={() => setDetailsOpen(true)}>جزئیات مأموریت</button>
      <button className="back-home-button" onClick={onHome}>بازگشت به خانه</button>
    </div>

    {detailsOpen && <div className="mission-detail-backdrop" role="presentation" onClick={() => setDetailsOpen(false)}>
      <section className="mission-detail-dialog" role="dialog" aria-modal="true" aria-labelledby="mission-detail-title" onClick={event => event.stopPropagation()}>
        <button className="mission-detail-close" aria-label="بستن جزئیات" onClick={() => setDetailsOpen(false)}>×</button>
        <h2 id="mission-detail-title">جزئیات مأموریت MIS-۱۰۴۹۸</h2>
        <p>حنا ونک ← علی رضایی</p>
        <p>شماره سفارش: HNA-۸۳۹۲۵۱</p>
        <p>تحویل موفق • ۲ بسته • ۳۱ دقیقه • ۴٫۲ کیلومتر</p>
        <button className="mission-detail-done" onClick={() => { setDetailsOpen(false); onNotice('جزئیات مأموریت MIS-۱۰۴۹۸ نمایش داده شد.') }}>متوجه شدم</button>
      </section>
    </div>}
  </section>
}
