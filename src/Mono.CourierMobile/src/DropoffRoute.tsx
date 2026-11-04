import { getNavigationUrl } from './navigation'

type DropoffRouteProps = { onNotice: (message: string) => void; onArrive: () => void }

export default function DropoffRoute({ onNotice, onArrive }: DropoffRouteProps) {
  const destinationAddress = 'یوسف‌آباد، خیابان شصت‌وچهارم، پلاک ۲۱'
  return <section className="content-scroll dropoff-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/dropoff-route-avatar.svg" alt="" />
      <div className="profile-copy"><strong>در مسیر تحویل</strong><small>MIS-۱۰۴۹۸ • حرکت به مقصد</small></div>
    </header>

    <section className="delivery-status-card">
      <div className="delivery-status-head"><span className="delivery-pill">در مسیر مقصد</span><strong>تحویل به علی رضایی</strong></div>
      <div className="delivery-metrics">
        <div><small>فاصله</small><b>۲٫۸ کیلومتر</b></div>
        <div><small>زمان</small><b>۱۸ دقیقه</b></div>
        <div><small>استاندارد خدمت</small><b className="safe">ایمن</b></div>
      </div>
    </section>

    <div className="dropoff-map" role="group" aria-label="نقشهٔ مسیر تا گیرنده در یوسف‌آباد">
      <div className="dropoff-road dr1"/><div className="dropoff-road dr2"/><div className="dropoff-road dr3"/><div className="dropoff-road dr4"/><div className="dropoff-road dr5"/><div className="dropoff-road dr6"/><div className="dropoff-road dr7"/><div className="dropoff-road dr8"/><div className="dropoff-road dr9"/>
      <div className="dropoff-block db1"/><div className="dropoff-block db2"/><div className="dropoff-block db3"/><div className="dropoff-block db4"/><div className="dropoff-block db5"/><div className="dropoff-block db6"/><div className="dropoff-block db7"/><div className="dropoff-block db8"/><div className="dropoff-block db9"/><div className="dropoff-block db10"/><div className="dropoff-block db11"/><div className="dropoff-block db12"/><div className="dropoff-block db13"/><div className="dropoff-block db14"/><div className="dropoff-block db15"/>
      <img className="dropoff-route-line" src="/figma/dropoff-route-line.svg" alt="" />
      <img className="dropoff-marker origin" src="/figma/dropoff-origin-marker.svg" alt="موقعیت پیک" />
      <img className="dropoff-marker destination" src="/figma/dropoff-destination-marker.svg" alt="مقصد تحویل" />
      <span className="dropoff-time-chip">۱۸ دقیقه تا مقصد</span>
      <span className="dropoff-map-caption">مسیر پیشنهادی مونو</span>
      <a className="navigation-button" href={getNavigationUrl(destinationAddress)} aria-label="مسیریابی به گیرنده">مسیریابی <span aria-hidden="true">↗</span></a>
    </div>

    <section className="recipient-card">
      <div className="recipient-head"><span className="recipient-pill">گیرنده</span><strong>علی رضایی</strong></div>
      <p className="recipient-address">{destinationAddress}</p>
      <div className="recipient-row"><span>HNA-۸۳۹۲۵۱</span><small>شماره سفارش</small></div>
      <div className="recipient-row"><span>۲ بسته • سالم</span><small>بسته همراه</small></div>
      <div className="recipient-actions">
        <button onClick={() => onNotice('تماس با گیرنده در نسخهٔ نمایشی فعال نیست.')}>تماس با گیرنده</button>
        <button onClick={() => onNotice('گزارش مشکل برای مأموریت ثبت شد.')}>گزارش مشکل</button>
      </div>
    </section>

    <button className="reached-destination-button" onClick={onArrive}>رسیدم به مقصد</button>
    <p className="destination-hint">پس از رسیدن، تحویل نهایی و تأیید نام گیرنده ثبت می‌شود.</p>
  </section>
}
