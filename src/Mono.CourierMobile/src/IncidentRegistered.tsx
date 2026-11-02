type IncidentRegisteredProps = { onNotice: (message: string) => void; onContinue: () => void }

export default function IncidentRegistered({ onNotice, onContinue }: IncidentRegisteredProps) {
  return <section className="content-scroll incident-registered-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>حادثه ثبت شد</strong><small>INC-۰۳۱۸ • در انتظار تصمیم</small></div>
    </header>

    <section className="incident-registered-hero">
      <div className="incident-registered-hero-head">
        <span>در انتظار پشتیبانی</span>
        <strong>پرونده حادثه باز است</strong>
      </div>
      <p>تا اعلام تصمیم، بسته را نزد خود نگه دار و وضعیت تحویل را تغییر نده.</p>
      <div className="incident-registered-metrics">
        <div><small>شناسه</small><b>INC-۰۳۱۸</b></div>
        <div><small>صف بررسی</small><b>عملیاتی</b></div>
        <div><small>وضعیت</small><b className="case-open">باز</b></div>
      </div>
    </section>

    <section className="incident-registered-card current-instruction-card">
      <h1>دستور فعلی</h1>
      <div className="incident-registered-row"><strong>نزد پیک</strong><span>بسته</span></div>
      <div className="incident-registered-row"><strong className="case-paused">متوقف</strong><span>تحویل به گیرنده</span></div>
      <div className="incident-registered-row"><strong className="case-blocked">در دسترس نیست</strong><span>لغو مستقیم</span></div>
    </section>

    <section className="incident-registered-card incident-case-summary">
      <h2>خلاصه پرونده</h2>
      <div className="incident-registered-row"><strong className="case-damage">آسیب‌دیدگی</strong><span>نوع حادثه</span></div>
      <div className="incident-registered-row"><strong className="case-ref">بسته ۱ از ۲</strong><span>بسته درگیر</span></div>
      <div className="incident-registered-row"><strong className="case-ref">HNA-۸۳۹۲۵۱</strong><span>سفارش</span></div>
      <div className="incident-registered-row"><strong>علی رضایی</strong><span>گیرنده</span></div>
    </section>

    <section className="incident-registered-card incident-case-tracking">
      <h2>پیگیری پرونده</h2>
      <div className="incident-timeline-row"><strong className="case-damage">حادثه ثبت شد</strong><span>۱۲:۲۸</span></div>
      <small>آسیب ظاهری بسته</small>
      <div className="incident-timeline-row"><strong className="case-paused">به صف پشتیبانی ارسال شد</strong><span>۱۲:۲۹</span></div>
      <small>در انتظار بررسی</small>
    </section>

    <div className="incident-registered-actions">
      <button onClick={() => onNotice('تماس با پشتیبانی در این پیش‌نمایش فعال نیست.')}>تماس با پشتیبانی</button>
      <button onClick={() => onNotice('جزئیات پرونده در این پیش‌نمایش ثبت شده است.')}>مشاهده پرونده</button>
    </div>
    <button className="incident-acknowledge-button" onClick={() => {
      onNotice('در پیش‌نمایش، تصمیم نمونهٔ پشتیبانی بازگشت سفارش به شعبه است؛ اتصال واقعی فعال نیست.')
      onContinue()
    }}>متوجه شدم؛ منتظر می‌مانم</button>
  </section>
}
