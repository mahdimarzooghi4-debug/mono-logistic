type PickupRouteProps = { onNotice: (message: string) => void }

export default function PickupRoute({ onNotice }: PickupRouteProps) {
  return <section className="content-scroll route-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/route-avatar.svg" alt="" />
      <div className="profile-copy"><strong>مأموریت فعال</strong><small>MIS-۱۰۴۹۸ • حرکت به مبدا</small></div>
    </header>

    <section className="active-status-card">
      <div className="route-status-head"><span className="route-pill">در مسیر مبدا</span><strong>فروشگاه حنا • شعبه مرکزی</strong></div>
      <div className="status-metrics">
        <div><small>فاصله</small><strong>۱٫۴ کیلومتر</strong></div>
        <div><small>زمان</small><strong>۶ دقیقه</strong></div>
        <div><small>آمادگی سفارش</small><strong className="ready">آماده</strong></div>
      </div>
    </section>

    <div className="route-map" role="img" aria-label="مسیر پیشنهادی مونو از موقعیت پیک تا فروشگاه حنا">
      <div className="route-road rr1"/><div className="route-road rr2"/><div className="route-road rr3"/><div className="route-road rr4"/><div className="route-road rr5"/><div className="route-road rr6"/>
      <div className="route-block rb1"/><div className="route-block rb2"/><div className="route-block rb3"/><div className="route-block rb4"/><div className="route-block rb5"/><div className="route-block rb6"/><div className="route-block rb7"/><div className="route-block rb8"/><div className="route-block rb9"/><div className="route-block rb10"/><div className="route-block rb11"/><div className="route-block rb12"/><div className="route-block rb13"/><div className="route-block rb14"/><div className="route-block rb15"/>
      <img className="route-line" src="/figma/route-line.svg" alt="" />
      <img className="route-marker route-marker-origin" src="/figma/route-origin.svg" alt="موقعیت پیک" />
      <img className="route-marker route-marker-destination" src="/figma/route-destination.svg" alt="فروشگاه حنا" />
      <span className="map-time-chip">۶ دقیقه تا مبدا</span>
      <span className="map-caption">مسیر پیشنهادی مونو</span>
    </div>

    <section className="pickup-info-card">
      <div className="pickup-info-head"><span className="ready-pill">آماده تحویل</span><strong>شعبه مرکزی حنا</strong></div>
      <p className="pickup-address">خیابان ولیعصر، بالاتر از میدان ونک</p>
      <div className="pickup-details">
        <div><small>شماره سفارش</small><b>HNA-۸۳۹۲۵۱</b></div>
        <div><small>تعداد بسته</small><b>۲</b></div>
        <div><small>زمان دریافت</small><b>حداکثر ۳ دقیقه</b></div>
      </div>
      <div className="pickup-actions">
        <button onClick={() => onNotice('تماس با شعبه در نسخهٔ نمایشی فعال نیست.')}>تماس با شعبه</button>
        <button onClick={() => onNotice('گزارش مشکل برای این مأموریت ثبت شد.')}>گزارش مشکل</button>
      </div>
    </section>

    <button className="arrived-button" onClick={() => onNotice('رسیدن به مبدأ ثبت شد؛ مرحلهٔ بعد دریافت سفارش است.')}>رسیدم به مبدا</button>
    <p className="arrival-hint">پس از رسیدن، زمان انتظار دریافت سفارش ثبت می‌شود.</p>
  </section>
}
