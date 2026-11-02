type ReturningToOriginProps = { onNotice: (message: string) => void; onArrive: () => void }

const roads = [
  { axis: 'horizontal', x: -21, y: 35 },
  { axis: 'horizontal', x: -21, y: 77 },
  { axis: 'horizontal tilted-left', x: -21, y: 73 },
  { axis: 'horizontal tilted-right', x: -22, y: 171 },
  { axis: 'horizontal', x: -21, y: 213 },
  { axis: 'vertical', x: 53, y: -21 },
  { axis: 'vertical tilted-right', x: 108, y: -21 },
  { axis: 'vertical', x: 201, y: -21 },
  { axis: 'vertical tilted-left', x: 299, y: -22 },
]

const blocks = [
  [17, 13, 64, 24], [105, 15, 72, 24], [227, 15, 88, 26],
  [21, 91, 78, 24], [143, 91, 60, 26], [249, 89, 66, 28],
  [23, 147, 60, 26], [111, 149, 88, 26], [237, 149, 78, 28],
  [27, 197, 88, 24], [153, 197, 58, 24], [251, 195, 64, 26],
]

export default function ReturningToOrigin({ onNotice, onArrive }: ReturningToOriginProps) {
  function confirmArrival() {
    onNotice('رسیدن به شعبه در پیش‌نمایش ثبت شد؛ تحویل برگشتی را تأیید کن.')
    onArrive()
  }

  return <section className="content-scroll return-origin-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/arrived-avatar.svg" alt="" />
      <div className="profile-copy"><strong>بازگشت به مبدا</strong><small>MIS-۱۰۴۹۸ • تصمیم پشتیبانی</small></div>
    </header>

    <section className="return-decision-card">
      <div className="return-decision-head"><span>تصمیم ثبت شد</span><strong>بازگرداندن سفارش به شعبه</strong></div>
      <p>بسته را نزد خود نگه دار و طبق مسیر زیر به شعبه مرکزی حنا برگردان.</p>
      <div className="return-reason-row"><strong>حادثه تحویل</strong><span>دلیل</span></div>
    </section>

    <section className="return-status-card" aria-label="اطلاعات مسیر بازگشت">
      <div><small>فاصله</small><strong>۳٫۴ کیلومتر</strong></div>
      <div><small>زمان</small><strong>۱۴ دقیقه</strong></div>
      <div><small>بسته</small><strong className="return-package-status">۲ • نزد پیک</strong></div>
    </section>

    <div className="return-map" role="img" aria-label="مسیر بازگشت ۱۴ دقیقه‌ای به شعبه مرکزی حنا">
      {roads.map((road, index) => <span key={`road-${index}`} className={`return-map-road ${road.axis}`} style={{ left: road.x, top: road.y }} />)}
      {blocks.map(([x, y, width, height], index) => <span key={`block-${index}`} className="return-map-block" style={{ left: x, top: y, width, height }} />)}
      <img className="return-route-line" src="/figma/return-route-line.svg" alt="" />
      <img className="return-route-origin" src="/figma/return-route-origin.svg" alt="" />
      <img className="return-route-destination" src="/figma/return-route-destination.svg" alt="" />
      <span className="return-map-time">۱۴ دقیقه تا شعبه</span>
    </div>

    <section className="return-origin-card">
      <div className="return-origin-head"><span>مبدا برگشت</span><strong>شعبه مرکزی حنا</strong></div>
      <p>خیابان ولیعصر، بالاتر از میدان ونک</p>
    </section>

    <div className="return-origin-actions">
      <button onClick={() => onNotice('تماس با شعبه در این پیش‌نمایش فعال نیست.')}>تماس با شعبه</button>
      <button className="return-arrived-button" onClick={confirmArrival}>رسیدم به مبدا</button>
    </div>
  </section>
}
