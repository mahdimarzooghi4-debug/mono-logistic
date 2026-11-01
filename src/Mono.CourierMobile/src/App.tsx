import { useRef, useState } from 'react'
import PickupRoute from './PickupRoute'

type Offer = { id: string; shop: string; recipient: string; amount: string; pickup: string; total: string; distance: string }
const offers: Offer[] = [
  { id: 'MIS-۱۰۵۱۲', shop: 'حنا ونک', recipient: 'سارا نادری', amount: '۲۷٬۵۰۰', pickup: '۵ دقیقه', total: '۱۹ دقیقه', distance: '۳٫۱ کیلومتر' },
  { id: 'MIS-۱۰۵۰۹', shop: 'حنا جردن', recipient: 'علی کاظمی', amount: '۳۱٬۱۵۰', pickup: '۷ دقیقه', total: '۲۳ دقیقه', distance: '۴ کیلومتر' },
  { id: 'MIS-۱۰۵۰۶', shop: 'حنا میرداماد', recipient: 'مریم مرادی', amount: '۲۴٬۹۲۰', pickup: '۴ دقیقه', total: '۱۶ دقیقه', distance: '۲٫۶ کیلومتر' },
]
const navItems = [
  { id: 'account', label: 'حساب', icon: 'nav-profile.svg' },
  { id: 'wallet', label: 'درآمد', icon: 'nav-wallet.svg' },
  { id: 'missions', label: 'ماموریت‌ها', icon: 'nav-missions.svg' },
  { id: 'home', label: 'خانه', icon: 'nav-home.svg' },
]

function MapPreview() {
  return <div className="map-preview" aria-label="نقشه نمایشی مسیر ماموریت">
    <div className="map-legend pickup-legend"><img src="/figma/legend-pickup.svg" alt="" /><span>مبدأ</span></div>
    <div className="map-legend dropoff-legend"><img src="/figma/legend-dropoff.svg" alt="" /><span>مقصد</span></div>
    <span className="map-pin pickup-pin"><img className="halo" src="/figma/pickup-halo.svg" alt="" /><img className="pin" src="/figma/pickup.svg" alt="" /></span>
    <span className="map-pin dropoff-pin"><img className="halo" src="/figma/dropoff-halo.svg" alt="" /><img className="pin" src="/figma/dropoff.svg" alt="" /></span>
    <div className="map-block b1"/><div className="map-block b2"/><div className="map-block b3"/><div className="map-block b4"/><div className="map-block b5"/><div className="map-block b6"/><div className="map-block b7"/>
    <div className="map-road rh1"/><div className="map-road rh2"/><div className="map-road rh3"/><div className="map-road rv1"/><div className="map-road rv2"/><div className="map-road rv3"/>
  </div>
}

export default function App() {
  const [online, setOnline] = useState(true)
  const [active, setActive] = useState('home')
  const [index, setIndex] = useState(0)
  const [accepted, setAccepted] = useState<string | null>(null)
  const [screen, setScreen] = useState<'home' | 'pickup'>('home')
  const [notice, setNotice] = useState('')
  const touchStart = useRef<number | null>(null)
  const offer = offers[index]

  function changeOffer(direction: number) {
    setIndex(current => (current + direction + offers.length) % offers.length)
  }

  function acceptOffer() {
    setAccepted(offer.id)
    setScreen('pickup')
    setActive('missions')
  }

  return <main className="courier-app" dir="rtl">
    {screen === 'pickup' ? <PickupRoute onNotice={message => {
      setNotice(message)
      window.setTimeout(() => setNotice(''), 2600)
    }} /> : <section className="content-scroll">
      <header className="profile-header">
        <img className="avatar" src="/figma/avatar.svg" alt="" />
        <div className="profile-copy"><strong>سلام، رضا</strong><small>پیک مونو • CR-۲۸۴</small></div>
      </header>

      <section className="availability-card">
        <button className={`online-toggle ${online ? 'is-online' : ''}`} onClick={() => setOnline(value => !value)} aria-pressed={online}>
          {online ? 'آنلاین' : 'آفلاین'}
        </button>
        <div className="availability-title"><strong>{online ? 'آماده دریافت ماموریت' : 'فعلا آفلاین هستید'}</strong><small>منطقه فعال: مرکزی</small></div>
        <div className="stats-row"><div><small>امتیاز</small><strong>۴٫۹</strong></div><div><small>ماموریت</small><strong>۸</strong></div><div><small>درآمد امروز</small><strong className="green">۲۶۷٬۰۰۰ تومان</strong></div></div>
      </section>

      <div className="section-title"><span className="offer-count">۳ پیشنهاد</span><h1>پیشنهادهای ماموریت</h1></div>

      <div className="offer-window" onTouchStart={event => { touchStart.current = event.touches[0]?.clientX ?? null }} onTouchEnd={event => {
        const end = event.changedTouches[0]?.clientX
        if (touchStart.current !== null && end !== undefined && Math.abs(end - touchStart.current) > 36) changeOffer(end > touchStart.current ? -1 : 1)
        touchStart.current = null
      }}>
        <article className="offer-card">
          <div className="offer-top"><span className="payout">{offer.amount} تومان</span><span className="mission-id">{offer.id}</span></div>
          <h2>{offer.shop} ← {offer.recipient}</h2>
          <p className="response-time">حداکثر زمان پاسخ: ۲۰ ثانیه</p>
          <MapPreview />
          <div className="route-metrics"><div><small>تا مبدأ</small><strong>{offer.pickup}</strong></div><div><small>کل مسیر</small><strong>{offer.total}</strong></div><div><small>مسافت</small><strong>{offer.distance}</strong></div></div>
          <p className="eligibility-note">محدوده و ظرفیت شما برای این ماموریت مجاز است.</p>
          <button className={`accept-button ${accepted === offer.id ? 'accepted' : ''}`} onClick={acceptOffer}>{accepted === offer.id ? 'ماموریت پذیرفته شد' : 'پذیرش این ماموریت'}</button>
        </article>
      </div>
      <div className="carousel-dots" aria-label={`پیشنهاد ${index + 1} از ۳`}>
        {offers.map((item, i) => <button key={item.id} aria-label={`نمایش پیشنهاد ${i + 1}`} aria-current={i === index} className={i === index ? 'selected' : ''} onClick={() => setIndex(i)} />)}
      </div>
      <p className="swipe-hint">برای دیدن دو پیشنهاد دیگر، کارت را به راست یا چپ بکش.</p>

      <section className="wallet-card"><span className="withdrawable">قابل برداشت</span><div><strong>کیف پول</strong><b>۴۸۶٬۵۰۰ تومان</b></div></section>
    </section>}
    {notice && <div className="toast" role="status">{notice}</div>}
    <nav className={`bottom-nav ${screen === 'pickup' ? 'missions-active' : ''}`} aria-label="ناوبری اصلی">
      {navItems.map(item => <button key={item.id} className={active === item.id ? 'active' : ''} onClick={() => {
        setActive(item.id)
        if (item.id === 'home') setScreen('home')
        if (item.id === 'missions') setScreen('pickup')
      }} aria-current={active === item.id ? 'page' : undefined}>
        <span className="nav-icon"><img src={screen === 'pickup' ? `/figma/route-nav-${item.id === 'account' ? 'account' : item.id === 'wallet' ? 'income' : item.id === 'missions' ? 'missions' : 'home'}.svg` : `/figma/${item.icon}`} alt="" /></span><small>{item.label}</small>
      </button>)}
    </nav>
  </main>
}
