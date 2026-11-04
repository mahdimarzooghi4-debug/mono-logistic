import { useState } from 'react'

type ReturnCompleteProps = { onHome: () => void; onNotice: (message: string) => void }

export default function ReturnComplete({ onHome, onNotice }: ReturnCompleteProps) {
  const [caseOpen, setCaseOpen] = useState(false)

  return <section className="content-scroll return-complete-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/return-complete/return-complete-avatar.svg" alt="" />
      <div className="profile-copy"><strong>برگشت تکمیل شد</strong><small>MIS-۱۰۴۹۸ • پایان مسیر برگشت</small></div>
    </header>
    <section className="return-complete-hero">
      <img src="/figma/return-complete/return-complete-check.svg" alt="" />
      <h1>تحویل برگشتی با موفقیت ثبت شد</h1>
      <p>بخش عملیاتی این مأموریت برای پیک بسته شد.</p>
    </section>
    <section className="return-complete-summary">
      <div className="return-complete-head"><span>تکمیل</span><h2>خلاصه برگشت</h2></div>
      <div className="return-complete-row"><strong>مرکزی حنا</strong><span>شعبه</span></div>
      <div className="return-complete-row"><strong>۲ بسته</strong><span>بسته برگشتی</span></div>
      <div className="return-complete-row"><strong>مهدی کاظمی</strong><span>تحویل‌گیرنده</span></div>
    </section>
    <section className="return-complete-case">
      <div className="return-complete-head"><span>ادامه بررسی</span><h2>پرونده حادثه مستقل ادامه دارد</h2></div>
      <div className="return-complete-row"><strong className="return-complete-teal">INC-۰۳۱۸</strong><span>پرونده</span></div>
      <div className="return-complete-row"><strong className="return-complete-waiting">در بررسی پشتیبانی</strong><span>وضعیت</span></div>
      <div className="return-complete-row"><strong className="return-complete-waiting">در انتظار بسته‌شدن پرونده</strong><span>تسویه مالی</span></div>
    </section>
    <section className="return-complete-availability">
      <div className="return-complete-head"><span>آنلاین</span><h2>آماده مأموریت بعدی</h2></div>
      <div className="return-complete-metrics">
        <div><small>منطقه</small><strong>مرکزی</strong></div>
        <div><small>مأموریت فعال</small><strong>۰</strong></div>
        <div><small>ظرفیت</small><strong className="return-complete-free">آزاد</strong></div>
      </div>
    </section>
    <div className="return-complete-actions">
      <button onClick={() => setCaseOpen(true)}>مشاهده پرونده</button>
      <button className="return-complete-home" onClick={onHome}>بازگشت به خانه</button>
    </div>
    {caseOpen && <div className="mission-detail-backdrop" role="presentation" onClick={() => setCaseOpen(false)}>
      <section className="mission-detail-dialog" role="dialog" aria-modal="true" aria-labelledby="return-case-title" onClick={event => event.stopPropagation()}>
        <button className="mission-detail-close" aria-label="بستن پرونده" onClick={() => setCaseOpen(false)}>×</button>
        <h2 id="return-case-title">پرونده حادثه INC-۰۳۱۸</h2>
        <p>پرونده پس از تحویل برگشتی نیز در بررسی پشتیبانی باقی می‌ماند.</p>
        <p>تسویه مالی پس از بسته‌شدن پرونده مشخص می‌شود.</p>
        <button className="mission-detail-done" onClick={() => { setCaseOpen(false); onNotice('پرونده حادثه INC-۰۳۱۸ هنوز در بررسی پشتیبانی است.') }}>متوجه شدم</button>
      </section>
    </div>}
  </section>
}
