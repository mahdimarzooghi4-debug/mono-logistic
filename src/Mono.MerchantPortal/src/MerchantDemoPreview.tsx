import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import MerchantNavIcon, { type MerchantNavId } from './MerchantNavIcon'
import MerchantBrandingSettings from './MerchantBrandingSettings'
import trackingMap from './assets/figma-tracking/tracking-map.svg'

type PreviewView = 'dashboard' | 'deliveries' | 'create' | 'tracking' | 'deliveryDetail' | 'branches' | 'billing' | 'settings'
type PreviewDelivery = { order: string; recipient: string; branch: string; status: string; mission: string; deadline: string }

const fa = new Intl.NumberFormat('fa-IR')
const initialDeliveries: PreviewDelivery[] = [
  { order: 'ORD-۰۳۸۴', recipient: 'علی رضایی', branch: 'ونک', status: 'در مسیر', mission: 'MIS-۱۰۵۱۲', deadline: '۱۲:۴۵' },
  { order: 'ORD-۰۳۸۲', recipient: 'سارا نادری', branch: 'جردن', status: 'دریافت', mission: 'MIS-۱۰۵۰۹', deadline: '۱۲:۵۲' },
  { order: 'ORD-۰۳۷۹', recipient: 'مریم مرادی', branch: 'میرداماد', status: 'در مسیر', mission: 'MIS-۱۰۵۰۶', deadline: '۱۳:۰۵' },
  { order: 'ORD-۰۳۷۴', recipient: 'رضا موسوی', branch: 'ونک', status: 'تحویل', mission: 'MIS-۱۰۴۹۸', deadline: '۱۲:۳۲' },
  { order: 'ORD-۰۳۷۱', recipient: 'آرش کاظمی', branch: 'جردن', status: 'نیازمند اقدام', mission: 'MIS-۱۰۴۹۳', deadline: '۱۲:۴۰' },
  { order: 'ORD-۰۳۶۸', recipient: 'الهام یوسفی', branch: 'میرداماد', status: 'در مسیر', mission: 'MIS-۱۰۵۰۲', deadline: '۱۳:۱۲' },
  { order: 'ORD-۰۳۶۵', recipient: 'پریسا کریمی', branch: 'ونک', status: 'دریافت', mission: 'MIS-۱۰۵۰۰', deadline: '۱۳:۲۰' },
  { order: 'ORD-۰۳۶۲', recipient: 'محمد نادری', branch: 'جردن', status: 'تحویل', mission: 'MIS-۱۰۵۰۰', deadline: '۱۳:۳۰' },
  { order: 'ORD-۰۳۵۷', recipient: 'نازنین احمدی', branch: 'ونک', status: 'تحویل', mission: 'MIS-۱۰۴۷۹', deadline: '۱۱:۳۲' },
  { order: 'ORD-۰۳۵۴', recipient: 'سهیل جلالی', branch: 'میرداماد', status: 'نیازمند اقدام', mission: 'MIS-۱۰۴۷۴', deadline: '۱۲:۱۰' },
]

const menu: Array<{ id: MerchantNavId; label: string }> = [
  { id: 'dashboard', label: 'داشبورد' }, { id: 'create', label: 'ارسال جدید' },
  { id: 'deliveries', label: 'ارسال‌ها' }, { id: 'tracking', label: 'رهگیری' },
  { id: 'branches', label: 'شعب' }, { id: 'billing', label: 'صورتحساب' },
  { id: 'settings', label: 'تنظیمات' },
]

export default function MerchantDemoPreview({ onExit }: { onExit: () => void }) {
  const [view, setView] = useState<PreviewView>('dashboard')
  const [deliveries, setDeliveries] = useState(initialDeliveries)
  const [tracking, setTracking] = useState('ORD-۰۳۸۴')
  const [trackingResult, setTrackingResult] = useState<PreviewDelivery | null>(initialDeliveries[0])
  const [notice, setNotice] = useState('')
  const [branchDrawer, setBranchDrawer] = useState(false)
  const [inviteDrawer, setInviteDrawer] = useState(false)
  const [endpointDrawer, setEndpointDrawer] = useState(false)
  const title: Record<PreviewView, string> = {
    dashboard: 'داشبورد کسب‌وکار', deliveries: 'ارسال‌های شما', create: 'ثبت ارسال جدید',
    tracking: 'رهگیری', deliveryDetail: 'جزئیات ارسال', branches: 'شعب', billing: 'صورتحساب', settings: 'تنظیمات هویت بصری',
  }
  const activeCount = useMemo(() => 24 + Math.max(0, deliveries.length - initialDeliveries.length), [deliveries])

  function createSample(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const order = String(form.get('order') || 'ORD-۰۳۹۰').trim()
    const recipient = String(form.get('recipient') || 'گیرندهٔ نمایشی').trim()
    setDeliveries(current => [{ order, recipient, branch: 'ونک', status: 'ثبت‌شده', mission: 'در انتظار تخصیص', deadline: '—' }, ...current])
    setNotice(`ارسال نمونهٔ ${order} فقط در همین پیش‌نمایش ثبت شد.`)
    setView('deliveries')
  }

  return <div className="app-shell demo-shell">
    <aside className="sidebar" aria-label="ناوبری اصلی نسخهٔ نمایشی">
      <div className="merchant-brand-card"><div className="brand-mark" aria-hidden="true">س</div><div><strong>فروشگاه سپهر</strong><small>پنل ارسال و لجستیک</small><em>Powered by Mono</em></div></div>
      <nav className="navigation">{menu.map((item) => <button key={item.id} className={`nav-item ${view === item.id ? 'active' : ''}`} onClick={() => { setView(item.id); setNotice(''); setBranchDrawer(false); setInviteDrawer(false); setEndpointDrawer(false) }}><span className="nav-icon"><MerchantNavIcon id={item.id} /></span>{item.label}</button>)}</nav>
      <div className="sidebar-footer demo-account"><div><strong>مدیر حساب سپهر</strong><small>Workspace فروشگاه سپهر</small><small className="demo-label sr-only">محیط نمایشی · داده‌های ساختگی</small></div><button className="sidebar-logout" onClick={onExit}>خروج</button></div>
    </aside>

    <main className={`workspace ${view === 'settings' ? 'workspace-branding' : ''}`}>
      {view !== 'settings' && <header className="topbar"><div className="topbar-actions">
        {view === 'dashboard' && <><button className="secondary-button topbar-action" onClick={() => setView('tracking')}>رهگیری سریع</button><button className="primary-button topbar-action" onClick={() => setView('create')}>ارسال جدید</button></>}
        {view === 'create' && <button className="secondary-button topbar-action" disabled>ذخیره پیش‌نویس</button>}
        {view === 'deliveryDetail' && <><button className="secondary-button topbar-action" disabled>پشتیبانی</button><button className="primary-button topbar-action" onClick={() => setView('create')}>ارسال جدید</button></>}
        {view === 'deliveries' && <><button className="secondary-button topbar-action" disabled>خروجی CSV</button><button className="primary-button topbar-action" onClick={() => setView('create')}>ارسال جدید</button></>}
        {view === 'tracking' && <button className="primary-button topbar-action" onClick={() => setView('create')}>ارسال جدید</button>}
        {view === 'branches' && <button className="primary-button topbar-action" onClick={() => setBranchDrawer(true)}>افزودن شعبه</button>}
        {view === 'billing' && <><button className="secondary-button topbar-action" disabled>خروجی CSV</button><button className="primary-button topbar-action" disabled>دانلود صورتحساب</button></>}
      </div><div className="page-title"><h1>{title[view]}</h1><p>{view === 'dashboard' ? 'فروشگاه سپهر • مدیریت ارسال‌های شهری و شعب' : 'فروشگاه سپهر • محیط نمایشی'}</p></div></header>}
      {notice && <div className="message success" role="status">{notice}</div>}
      {branchDrawer && <DemoBranchDrawer onClose={() => setBranchDrawer(false)} onSave={() => { setBranchDrawer(false); setNotice('شعبه فقط در پیش‌نمایش محلی اضافه شد؛ حساب واقعی تغییری نکرد.') }} />}
      {inviteDrawer && <DemoInviteDrawer onClose={() => setInviteDrawer(false)} onSave={() => { setInviteDrawer(false); setNotice('دعوت فقط در پیش‌نمایش محلی ثبت شد؛ برای Workspace واقعی ارسال نشد.') }} />}
      {endpointDrawer && <DemoWebhookDrawer onClose={() => setEndpointDrawer(false)} onSave={() => { setEndpointDrawer(false); setNotice('Endpoint فقط در پیش‌نمایش محلی اضافه شد؛ به سامانهٔ واقعی متصل نیست.') }} />}

      {view === 'dashboard' && <>
        <section className="demo-kpi-strip" aria-label="شاخص‌های نمایشی"><DemoKpi label="ارسال فعال" value={fa.format(activeCount)} /><DemoKpi label="نیازمند اقدام" value="۲" tone="red" /><DemoKpi label="تحویل امروز" value="۵۸" tone="green" /><DemoKpi label="شعب فعال" value="۳" /><DemoKpi label="میانگین تحویل" value="۲۶ دقیقه" /></section>
        <div className="merchant-dashboard">
        <div className="dashboard-main-column">
          <section className="panel demo-active-panel"><div className="active-panel-heading"><span className="badge">{fa.format(activeCount)} فعال</span><h2>ارسال‌های فعال</h2></div><PreviewTable items={deliveries.slice(0, 7)} onSelect={item => { setTrackingResult(item); setView('deliveryDetail') }} /></section>
          <section className="panel demo-alerts"><div className="exceptions-heading"><span className="badge danger">۲ مورد</span><h2>موارد نیازمند اقدام</h2></div><div className="issue-row"><span className="badge open">باز</span><span className="issue-age">پشتیبانی در حال بررسی</span><div><strong>گیرنده در دسترس نیست</strong><small>ORD-۰۳۷۱ • MIS-۱۰۴۹۳</small></div></div><div className="issue-row"><span className="badge open">باز</span><span className="issue-age">بیش از ۳ دقیقه</span><div><strong>تأخیر آماده‌سازی شعبه</strong><small>ORD-۰۳۸۲ • MIS-۱۰۵۰۹</small></div></div></section>
        </div>
        <aside className="dashboard-aside">
          <section className="panel dashboard-widget quick-create"><h2>ارسال جدید</h2><p>مبدا، مقصد و بسته را وارد کن؛ مونو قبل از پذیرش، امکان‌سنجی، کرایه و تعهد تحویل را محاسبه می‌کند.</p><div className="quick-create-fields"><button onClick={() => setView('create')}><small>مبدا</small><span>شعبه را انتخاب کن</span></button><button onClick={() => setView('create')}><small>مقصد</small><span>آدرس گیرنده</span></button></div><button className="primary-button full-width" onClick={() => setView('create')}>شروع ثبت ارسال</button></section>
          <section className="panel dashboard-widget quick-track"><h2>رهگیری سریع</h2><form className="quick-track-form" onSubmit={event => { event.preventDefault(); setTrackingResult(deliveries.find(item => item.order === tracking || item.mission === tracking) || null); setView('tracking') }}><input aria-label="شناسهٔ سفارش یا مأموریت" placeholder="شناسه سفارش یا مأموریت" value={tracking} onChange={event => setTracking(event.target.value)} /><button className="secondary-button">جست‌وجو</button></form></section>
          <section className="panel dashboard-widget branch-widget"><h2>وضعیت شعب</h2><ul className="branch-status-list">{['ونک', 'جردن', 'میرداماد'].map(name => <li key={name}><span className="branch-active">فعال</span><span>{name}</span></li>)}</ul></section>
          <section className="panel dashboard-widget service-widget"><h2>سرویس فعال</h2><dl><div><dt>سرویس</dt><dd>ارسال شهری</dd></div><div><dt>حد شعاع</dt><dd>۵ کیلومتر</dd></div><div><dt>حد بسته در مسیر</dt><dd>۵</dd></div><div><dt>تعهد دریافت</dt><dd>حداکثر ۲۶ دقیقه</dd></div><div><dt>شعب فعال</dt><dd>۳</dd></div></dl><div className="service-note"><strong>مدیریت ارسال با مونو</strong><span>تعهد تحویل، تخصیص پیک، مسیر و استثناها توسط مونو مدیریت می‌شود.</span></div></section>
        </aside>
        </div>
      </>}

      {view === 'deliveries' && <DemoDeliveriesList items={deliveries} onSelect={item => { setTrackingResult(item); setView('deliveryDetail') }} />}
      {view === 'create' && <div className="create-workspace demo-create-workspace">
        <nav className="create-steps" aria-label="مراحل ثبت ارسال"><div className="current"><span>۱. اطلاعات ارسال</span></div><div><span>۲. امکان‌سنجی و قیمت</span><small>پس از ثبت اطلاعات</small></div><div><span>۳. تأیید و ثبت</span><small>پیش‌نمایش محلی</small></div></nav>
        <form className="create-layout" onSubmit={createSample}>
          <div className="create-form-column">
            <section className="panel create-form-section"><h2>مبدا و زمان آمادگی</h2><div className="create-fields two">
              <label>شعبهٔ مبدا<select name="branch" defaultValue="ونک"><option>ونک</option><option>جردن</option><option>میرداماد</option></select></label>
              <label>زمان تقریبی آماده‌شدن<input type="datetime-local" lang="fa-IR" dir="rtl" defaultValue="2026-11-02T12:30" /></label>
              <label className="full">آدرس مبدا<input defaultValue="تهران، ونک، خیابان ملاصدرا" /></label>
            </div></section>
            <section className="panel create-form-section"><h2>گیرنده و مقصد</h2><div className="create-fields two">
              <label>نام گیرنده<input name="recipient" defaultValue="مشتری نمونه" /></label><label>شماره موبایل گیرنده<input inputMode="tel" defaultValue="۰۹۱۲ ۵۵۵ ۸۴۳۱" /></label>
              <label className="full">آدرس مقصد<input defaultValue="تهران، خیابان جردن، خیابان سرو، پلاک ۱۲" /></label>
              <label>مختصات<input defaultValue="۳۵٫۷۵۲۱، ۵۱٫۴۰۹۸" /></label><label>یادداشت تحویل<input defaultValue="تحویل در ورودی ساختمان" /></label>
            </div></section>
            <section className="panel create-form-section"><h2>مشخصات بسته</h2><div className="create-fields three">
              <label>تعداد کیسه<input type="number" min="1" defaultValue="۲" /></label><label>وزن تقریبی<input defaultValue="۴ کیلوگرم" /></label><label>رده بار<select defaultValue="M"><option value="S">کوچک</option><option value="M">متوسط</option><option value="L">بزرگ</option></select></label>
              <label className="full">سرویس<select disabled defaultValue="URBAN_DELIVERY"><option value="URBAN_DELIVERY">ارسال شهری</option></select></label>
            </div><div className="handling-options"><label><input type="checkbox" defaultChecked />مایعات</label><label><input type="checkbox" />شکننده</label><label><input type="checkbox" defaultChecked />حمل عمودی</label><label><input type="checkbox" />نیازمند سرمایش</label></div></section>
            <section className="panel create-form-section"><h2>شناسه و مرجع کسب‌وکار</h2><div className="create-fields two"><label>شناسه سفارش داخلی<input name="order" required defaultValue="ORD-۰۳۹۰" /></label><label>مرجع خارجی — اختیاری<input defaultValue="INV-۱۴۰۵-۰۹۱" /></label></div><p className="capability-note">این شناسه فقط در داده‌های همین پیش‌نمایش نگه داشته می‌شود.</p></section>
          </div>
          <aside className="create-result-column">
            <section className="panel create-result-card feasibility-card"><div className="create-card-heading"><h2>نتیجه امکان‌سنجی</h2><span className="badge success">قابل پذیرش</span></div><DemoSummary label="فاصله مسیر" value="۳٫۸ کیلومتر" /><DemoSummary label="شیوه حمل پیشنهادی" value="موتورسیکلت" /><DemoSummary label="تعهد تحویل" value="تا ۱۲:۵۸" /><DemoSummary label="کرایه" value="۲۵۰٬۰۰۰ تومان" /></section>
            <section className="panel create-result-card policy-card"><h2>کنترل سیاست‌ها</h2><ul><li><i />پوشش منطقه<small>مبدا و مقصد در محدوده سرویس</small></li><li><i />حد شعاع<small>۳٫۸ از ۵ کیلومتر</small></li><li><i />ظرفیت و شیوه حمل<small>وزن و تعداد بسته قابل پذیرش</small></li><li><i />زمان آمادگی<small>امروز، ساعت ۱۲:۳۰</small></li></ul></section>
            <section className="panel create-result-card pricing-card"><h2>قیمت و تعهد</h2><DemoSummary label="کرایه محاسبه‌شده" value="۲۵۰٬۰۰۰ تومان" /><DemoSummary label="مبنای قیمت" value="قرارداد فعال فروشگاه سپهر" /><DemoSummary label="تحویل هدف" value="۱۲:۵۸" /><p>این نتیجهٔ نمونه فقط برای نمایش چیدمان است و قیمت یا تعهد واقعی نیست.</p></section>
            <section className="panel create-result-card evidence-card"><h2>شواهد تصمیم</h2><p>تصمیم نمونهٔ پذیرش بر پایهٔ سرویس، محدوده، وزن بسته و زمان آمادگی ثبت شده است.</p><small>Evidence ID: DEC-۴۲A</small></section>
            <div className="create-submit-actions"><button type="button" className="secondary-button" onClick={() => setView('dashboard')}>بازگشت و ویرایش</button><button className="primary-button">تأیید و ثبت ارسال</button></div>
          </aside>
        </form>
      </div>}
      {view === 'tracking' && <DemoTrackingPage tracking={tracking} setTracking={setTracking} deliveries={deliveries} selected={trackingResult} onSelect={setTrackingResult} onDetails={() => setView('deliveryDetail')} />}
      {view === 'deliveryDetail' && trackingResult && <DemoDeliveryDetail item={trackingResult} />}
      {view === 'branches' && <DemoBranchesPage />}
      {view === 'billing' && <DemoBillingPage />}
      {view === 'settings' && <MerchantBrandingSettings mode="demo" onDemoSave={() => setNotice('تنظیمات فقط در حافظهٔ پیش‌نمایش نمایشی نگه داشته شد؛ Workspace واقعی تغییر نکرد.')} onInvite={() => setInviteDrawer(true)} onAddEndpoint={() => setEndpointDrawer(true)} />}
    </main>
  </div>
}

function DemoTrackingPage({ tracking, setTracking, deliveries, selected, onSelect, onDetails }: {
  tracking: string; setTracking: (value: string) => void; deliveries: PreviewDelivery[]; selected: PreviewDelivery | null
  onSelect: (item: PreviewDelivery | null) => void; onDetails: () => void
}) {
  function search(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    onSelect(deliveries.find((item) => item.order === tracking || item.mission === tracking) || null)
  }
  const recent = [selected, ...deliveries.filter((item) => item !== selected)].filter((item): item is PreviewDelivery => Boolean(item)).slice(0, 3)
  return <main className="merchant-tracking-page demo-tracking-page" dir="rtl">
    <section className="tracking-search-card"><h2>چه چیزی را می‌خواهی رهگیری کنی؟</h2><form className="tracking-search-row" onSubmit={search}><label className="sr-only" htmlFor="demo-tracking-id">شناسهٔ سفارش یا مأموریت</label><input id="demo-tracking-id" value={tracking} onChange={(event) => setTracking(event.target.value)} placeholder="شماره سفارش یا مأموریت نمونه" required /><button className="primary-button">جست‌وجو</button></form></section>
    {selected ? <>
      <section className="tracking-result-metrics"><DetailMetric label="وضعیت نمونه" value={selected.status} /><DetailMetric label="تعهد تحویل نمونه" value={`تا ${selected.deadline}`} /><DetailMetric label="ETA نمونه" value="۱۲ دقیقه" /><DetailMetric label="پیک نمونه" value="CR-۲۸۴" /><DetailMetric label="استاندارد خدمت نمونه" value="ایمن" /></section>
      <div className="tracking-result-columns"><section className="tracking-live-card"><div className="tracking-card-heading"><div><strong>{selected.order} · {selected.mission}</strong><small>{selected.recipient} · شعبهٔ {selected.branch} · نمونه</small></div><span className="badge info">نمایشی</span></div><img className="tracking-map-illustration" src={trackingMap} alt="نقشه و مسیر نمونه برای پیش‌نمایش طراحی" /></section>
        <aside className="tracking-side-column"><section className="tracking-summary-card"><h2>خلاصهٔ ارسال · نمونه</h2><TrackingSummaryLine label="سفارش" value={selected.order} /><TrackingSummaryLine label="مأموریت" value={selected.mission} /><TrackingSummaryLine label="گیرنده" value={selected.recipient} /><TrackingSummaryLine label="شعبه" value={selected.branch} /><TrackingSummaryLine label="پیک" value="رضا احمدی · CR-۲۸۴" /><TrackingSummaryLine label="آخرین بروزرسانی" value="همین حالا · نمونه" /></section><section className="tracking-actions-card"><h2>اقدام</h2><button className="primary-button" onClick={onDetails}>باز کردن جزئیات ارسال</button><button className="secondary-button" disabled title="پشتیبانی در پیش‌نمایش نمایشی متصل نیست">تماس با پشتیبانی</button><small>این صفحه فقط برای مرور چیدمان است.</small></section></aside></div>
      <div className="tracking-lower-columns"><section className="tracking-timeline-card"><div className="tracking-card-heading"><strong>آخرین رویدادها</strong><span className="badge info">نمونه</span></div><ol className="tracking-event-list"><li><time>۱۲:۴۱</time><div><strong>در مسیر مقصد</strong><small>ETA فعلی ۱۲ دقیقه · نمونه</small></div></li><li><time>۱۲:۴۰</time><div><strong>سفارش دریافت شد</strong><small>۲ بسته از شعبهٔ {selected.branch} · نمونه</small></div></li><li><time>۱۲:۳۳</time><div><strong>پیک تخصیص داده شد</strong><small>رضا احمدی · CR-۲۸۴ · نمونه</small></div></li><li><time>۱۲:۳۲</time><div><strong>ارسال ثبت شد</strong><small>تعهد و کرایه نمونه قفل شد</small></div></li></ol></section>
        <section className="tracking-recent-card"><h2>جست‌وجوهای اخیر · نمونه</h2>{recent.map((item) => <button key={item.order} onClick={() => { setTracking(item.order); onSelect(item) }}><span className="badge info">{item.status}</span><b>{item.recipient}<small>{item.order} · {item.mission}</small></b></button>)}</section></div>
    </> : <section className="tracking-empty-card"><strong>ارسالی با این شناسه در داده‌های نمونه پیدا نشد</strong><p>شناسهٔ سفارش یا مأموریت یکی از ردیف‌های نمونه را وارد کنید.</p></section>}
  </main>
}

function TrackingSummaryLine({ label, value }: { label: string; value: string }) {
  return <div className="tracking-summary-line"><span>{label}</span><strong>{value}</strong></div>
}

function DemoKpi({ label, value, tone = 'cyan' }: { label: string; value: string; tone?: string }) {
  return <article className={`demo-kpi ${tone}`}><small>{label}</small><strong>{value}</strong></article>
}

function DemoBranchesPage() {
  const branches = [
    ['ونک','مهدی کاظمی','ونک','۸–۲۲','بله','فعال'],
    ['جردن','سارا جوادی','جردن','۸–۲۲','بله','فعال'],
    ['میرداماد','حسین قربانی','میرداماد','۹–۲۲','بله','فعال'],
    ['یوسف‌آباد','الهام مرادی','یوسف‌آباد','۸–۲۲','بله','فعال'],
    ['پاسداران','رضا شریفی','پاسداران','—','خیر','غیرفعال'],
  ]
  return <main className="demo-branches-page" dir="rtl">
    <section className="demo-branch-kpis" aria-label="خلاصه شعب"><Metric label="کل شعب" value="۵" /><Metric label="فعال" value="۴" tone="green" /><Metric label="موقتاً غیرفعال" value="۱" tone="orange" /><Metric label="آماده ارسال" value="۴" tone="cyan" /><Metric label="ساعات سرویس امروز" value="۸ تا ۲۲" /></section>
    <section className="demo-branch-filters"><label>منطقه<select defaultValue="همه مناطق"><option>همه مناطق</option><option>ونک</option></select></label><label>آمادگی ارسال<select defaultValue="همه"><option>همه</option><option>آماده</option></select></label><label>وضعیت<select defaultValue="همه وضعیت‌ها"><option>همه وضعیت‌ها</option><option>فعال</option></select></label><label className="branch-filter-search">جست‌وجو<input placeholder="نام، منطقه یا مسئول شعبه" /></label><button className="secondary-button">اعمال فیلتر</button></section>
    <div className="demo-branch-columns"><section className="panel demo-branch-table-panel"><div className="demo-table-scroll"><table className="demo-branch-table"><thead><tr><th>عملیات</th><th>شعبه</th><th>مسئول</th><th>منطقه</th><th>ساعات کاری</th><th>آماده ارسال</th><th>وضعیت</th></tr></thead><tbody>{branches.map((row) => <tr key={row[0]}><td><button type="button" className="link-button">باز</button></td><td><strong>{row[0]}</strong></td><td>{row[1]}</td><td>{row[2]}</td><td>{row[3]}</td><td className={row[4] === 'بله' ? 'good' : 'bad'}>{row[4]}</td><td><span className={`badge ${row[5] === 'فعال' ? 'success' : 'warning'}`}>{row[5]}</span></td></tr>)}</tbody></table></div><p className="data-note">فهرست و اعداد برای بازبینی طراحی نمونه هستند.</p></section>
      <aside className="demo-branch-detail"><section className="panel"><div className="panel-heading"><h2>شعبه ونک</h2><span className="badge success">فعال</span></div><DemoSummary label="کد شعبه" value="BR-۰۱" /><DemoSummary label="مسئول شعبه" value="مهدی کاظمی" /><DemoSummary label="وضعیت ارسال" value="آماده" /></section><section className="panel"><h2>موقعیت و ساعات کاری</h2><DemoSummary label="آدرس" value="ونک، ملاصدرا، پلاک ۲۱" /><DemoSummary label="مختصات" value="۳۵٫۷۵۲۱، ۵۱٫۴۰۹۸" /><DemoSummary label="ساعات کاری" value="هر روز ۸ تا ۲۲" /><DemoSummary label="تماس" value="۰۲۱ ۸۸۷۷ ۱۲۳۴" /></section><section className="panel"><h2>تنظیمات مبدا ارسال</h2><DemoSummary label="زمان پیش‌فرض آمادگی" value="۱۰ دقیقه" /><DemoSummary label="حد انتظار پیک" value="۳ دقیقه" /><DemoSummary label="شعاع سرویس" value="۵ کیلومتر" /><DemoSummary label="تحویل بسته" value="پیشخوان ارسال" /><button className="secondary-button full-width" disabled>ویرایش شعبه</button></section></aside>
    </div>
  </main>
}

function Metric({ label, value, tone = '' }: { label: string; value: string; tone?: string }) {
  return <article><small>{label}</small><strong className={tone}>{value}</strong></article>
}

function DemoBillingPage() {
  const invoices = [
    ['INV-۱۴۰۵-۰۹','شهریور ۱۴۰۵','۷۲','۱۴۰۵/۰۷/۱۰','۲٬۴۸۰٬۰۰۰','باز'],
    ['INV-۱۴۰۵-۰۸','مرداد ۱۴۰۵','۶۸','۱۴۰۵/۰۶/۱۰','۲٬۳۸۰٬۰۰۰','پرداخت‌شده'],
    ['INV-۱۴۰۵-۰۷','تیر ۱۴۰۵','۷۴','۱۴۰۵/۰۵/۱۰','۲٬۵۸۰٬۰۰۰','پرداخت‌شده'],
    ['INV-۱۴۰۵-۰۶','خرداد ۱۴۰۵','۶۳','۱۴۰۵/۰۴/۱۰','۲٬۲۱۰٬۰۰۰','پرداخت‌شده'],
  ]
  return <main className="demo-billing-page" dir="rtl">
    <section className="demo-billing-kpis"><Metric label="موعد بعدی" value="۱۴۰۵/۰۷/۱۰" /><Metric label="پرداخت‌شده این ماه" value="۲٬۹۶۰٬۰۰۰ تومان" tone="green" /><Metric label="ارسال‌های دوره" value="۷۲" tone="cyan" /><Metric label="مبلغ قابل پرداخت" value="۲٬۴۸۰٬۰۰۰ تومان" /><Metric label="صورتحساب باز" value="۱" tone="orange" /></section>
    <section className="demo-billing-filters"><label>سال مالی<select defaultValue="۱۴۰۵"><option>۱۴۰۵</option></select></label><label>جست‌وجو<input placeholder="Invoice / Order / Mission" /></label><label>نوع رکورد<select defaultValue="همه"><option>همه</option></select></label><label>شعبه<select defaultValue="همه شعب"><option>همه شعب</option></select></label><label>وضعیت<select defaultValue="همه وضعیت‌ها"><option>همه وضعیت‌ها</option></select></label><label>دوره<select defaultValue="شهریور ۱۴۰۵"><option>شهریور ۱۴۰۵</option></select></label></section>
    <div className="demo-billing-columns"><section className="panel demo-invoice-table-panel"><div className="panel-heading"><h2>صورتحساب‌های دوره‌ای</h2><span className="badge info">۴ دوره</span></div><div className="demo-table-scroll"><table className="demo-invoice-table"><thead><tr><th>عملیات</th><th>شماره صورتحساب</th><th>دوره</th><th>ارسال</th><th>موعد</th><th>مبلغ</th><th>وضعیت</th></tr></thead><tbody>{invoices.map((row) => <tr key={row[0]}><td><button className="link-button">باز</button></td><td><a>{row[0]}</a></td><td>{row[1]}</td><td>{row[2]}</td><td>{row[3]}</td><td>{row[4]} تومان</td><td><span className={`badge ${row[5] === 'باز' ? 'warning' : 'success'}`}>{row[5]}</span></td></tr>)}</tbody></table></div></section>
      <aside className="demo-invoice-detail"><section className="panel"><div className="panel-heading"><h2>INV-۱۴۰۵-۰۹</h2><span className="badge warning">باز</span></div><DemoSummary label="دوره" value="شهریور ۱۴۰۵" /><DemoSummary label="تعداد ارسال" value="۷۲" /><DemoSummary label="موعد پرداخت" value="۱۴۰۵/۰۷/۱۰" /><DemoSummary label="مبلغ قابل پرداخت" value="۲٬۴۸۰٬۰۰۰ تومان" /></section><section className="panel"><h2>خلاصهٔ هزینه</h2><DemoSummary label="کرایه ارسال‌ها" value="۲٬۵۲۰٬۰۰۰ تومان" /><DemoSummary label="اصلاحات / اعتبار" value="−۴۰٬۰۰۰ تومان" /><DemoSummary label="مبنای قرارداد" value="طبق قرارداد" /><DemoSummary label="مبلغ نهایی" value="۲٬۴۸۰٬۰۰۰ تومان" /></section><section className="panel"><div className="panel-heading"><h2>تطبیق ارسال‌ها</h2><span className="badge info">۷۲ رکورد</span></div><p className="data-note">جزئیات نمونهٔ ارسال‌ها</p></section></aside>
    </div>
  </main>
}

function PreviewTable({ items, onSelect }: { items: PreviewDelivery[]; onSelect: (item: PreviewDelivery) => void }) {
  return <div className="table-wrap"><table className="demo-delivery-table"><thead><tr><th>وضعیت</th><th>گیرنده</th><th>شعبه</th><th>تعهد تحویل</th><th>مأموریت</th><th>سفارش</th></tr></thead><tbody>{items.map(item => <tr key={item.order} onClick={() => onSelect(item)} tabIndex={0} onKeyDown={event => { if (event.key === 'Enter') onSelect(item) }}><td><span className={`badge ${item.status === 'تحویل' ? 'success' : item.status === 'دریافت' ? 'received' : item.status === 'نیازمند اقدام' ? 'danger' : 'info'}`}>{item.status}</span></td><td>{item.recipient}</td><td>{item.branch}</td><td>{item.deadline}</td><td dir="ltr">{item.mission}</td><td dir="ltr">{item.order}</td></tr>)}</tbody></table></div>
}

function DemoDeliveryDetail({ item }: { item: PreviewDelivery }) {
  return <main className="delivery-detail-page demo-delivery-detail" dir="rtl">
    <div className="delivery-detail-banner"><span className="detail-banner-icon">✓</span><div><strong>جزئیات ارسال</strong><span>{item.mission} · رهگیری زندهٔ نمونه · {item.order}</span></div><span className="badge success">ثبت شد</span></div>
    <section className="delivery-detail-metrics"><DetailMetric label="استاندارد خدمت" value="ایمن" /><DetailMetric label="پیک" value="CR-۲۸۴" /><DetailMetric label="بازه زمانی" value="۶ دقیقه" /><DetailMetric label="تعهد تحویل" value={`تا ${item.deadline}`} /><DetailMetric label="وضعیت" value={item.status} /></section>
    <div className="delivery-detail-columns"><div className="delivery-detail-main">
      <section className="panel delivery-map-panel"><div className="panel-heading"><div><span className="eyebrow">نقشهٔ نمونه</span><h2>مسیر ارسال</h2></div><span className="panel-meta">موقعیت زندهٔ نمونه</span></div><div className="demo-route-map" role="img" aria-label={`مسیر نمونه از شعبهٔ ${item.branch} تا مقصد گیرنده`}><svg viewBox="0 0 800 330" preserveAspectRatio="none" aria-hidden="true"><path className="route-underlay" d="M95 260 C180 230 225 205 300 182 S445 140 520 125 S650 74 710 58"/><path className="route-line" d="M95 260 C180 230 225 205 300 182 S445 140 520 125 S650 74 710 58"/><circle className="route-origin" cx="95" cy="260" r="11"/><circle className="route-courier" cx="405" cy="158" r="10"/><circle className="route-destination" cx="710" cy="58" r="12"/></svg><span className="route-label route-start">مبدأ شعبه {item.branch}</span><span className="route-label route-rider">CR-۲۸۴ · در مسیر</span><span className="route-label route-end">مقصد گیرنده</span></div></section>
      <section className="panel delivery-timeline-panel"><div className="panel-heading"><div><span className="eyebrow">سابقهٔ ارسال</span><h2>رویدادها</h2></div><span className="panel-meta">دادهٔ نمونه</span></div><ol className="delivery-event-list"><li><span className="event-marker" /><div><strong>درخواست ارسال ثبت شد</strong><small>۱۲:۱۶ · نمایشی</small></div></li><li><span className="event-marker" /><div><strong>پیک تخصیص یافت</strong><small>۱۲:۲۳ · نمایشی</small></div></li><li><span className="event-marker" /><div><strong>ارسال در مسیر است</strong><small>{item.deadline} · نمایشی</small></div></li></ol></section>
    </div><aside className="delivery-detail-side">
      <section className="panel delivery-summary-panel"><div className="panel-heading"><div><span className="eyebrow">خلاصه</span><h2>اطلاعات ارسال</h2></div></div><DemoSummary label="شماره سفارش" value={item.order} /><DemoSummary label="مأموریت" value={item.mission} /><DemoSummary label="کرایه" value="۲۵۰٬۰۰۰ تومان" /><DemoSummary label="تعهد تحویل" value={`تا ${item.deadline}`} /></section>
      <section className="panel delivery-recipient-panel"><div className="panel-heading"><div><span className="eyebrow">مرسوله</span><h2>گیرنده و بسته</h2></div></div><DemoSummary label="گیرنده" value={item.recipient} /><DemoSummary label="موبایل" value="۰۹۱۲ ۵۵۵ ۸۴۳۱" /><DemoSummary label="مقصد" value="تهران، خیابان جردن، خیابان سرو" /><DemoSummary label="تعداد بسته" value="۲" /><DemoSummary label="حمل" value="عمودی · مایعات" /></section>
      <section className="panel delivery-actions-panel"><div className="panel-heading"><div><span className="eyebrow">اقدام‌ها</span><h2>اقدام و پشتیبانی</h2></div></div><div className="demo-incident-note"><strong>مانع جدی ثبت نشده</strong><small>تا قبل از تحویل، درخواست لغو ارسال ثبت کنید.</small></div><DemoSummary label="استاندارد خدمت" value="ایمن" /><DemoSummary label="آخرین بروزرسانی" value="همین حالا" /><DemoSummary label="شواهد تصمیم" value="DEC-۴۲A" /><div className="delivery-action-buttons"><button className="secondary-button" disabled>تماس با پشتیبانی</button><button className="danger-outline-button" disabled>درخواست لغو</button></div><button className="primary-button" disabled>مشاهده جزئیات کامل مأموریت</button></section>
    </aside></div>
  </main>
}

function DemoDeliveriesList({ items, onSelect }: { items: PreviewDelivery[]; onSelect: (item: PreviewDelivery) => void }) {
  const [draft, setDraft] = useState({ query: '', status: '', branch: '' })
  const [filters, setFilters] = useState({ query: '', status: '', branch: '' })
  const visibleItems = items.filter((item) => (!filters.status || item.status === filters.status) && (!filters.branch || item.branch === filters.branch) && (!filters.query || `${item.order} ${item.mission} ${item.recipient}`.toLocaleLowerCase().includes(filters.query.trim().toLocaleLowerCase())))
  const statuses = Array.from(new Set(items.map((item) => item.status)))
  return <main className="merchant-deliveries-page demo-deliveries-page" dir="rtl">
    <section className="delivery-list-metrics" aria-label="شاخص‌های نمونه"><DetailMetric label="لغوشده امروز · نمونه" value="۳" /><DetailMetric label="نیازمند اقدام · نمونه" value="۲" /><DetailMetric label="تحویل امروز · نمونه" value="۵۸" /><DetailMetric label="فعال · نمونه" value="۲۴" /><DetailMetric label="همه ارسال‌ها · نمونه" value="۴۸۲" /></section>
    <section className="panel delivery-list-filter-panel"><div className="delivery-list-filters demo-delivery-filters">
      <label className="delivery-search-field">جست‌وجو<input value={draft.query} onChange={(event) => setDraft((current) => ({ ...current, query: event.target.value }))} placeholder="سفارش، مأموریت یا گیرندهٔ نمونه" /></label>
      <label>وضعیت<select value={draft.status} onChange={(event) => setDraft((current) => ({ ...current, status: event.target.value }))}><option value="">همهٔ وضعیت‌ها</option>{statuses.map((value) => <option key={value}>{value}</option>)}</select></label>
      <label>شعبه<select value={draft.branch} onChange={(event) => setDraft((current) => ({ ...current, branch: event.target.value }))}><option value="">همهٔ شعب</option>{Array.from(new Set(items.map((item) => item.branch))).map((branch) => <option key={branch}>{branch}</option>)}</select></label>
      <label>بازه زمانی<select defaultValue="۷ روز اخیر"><option>۷ روز اخیر</option><option>۳۰ روز اخیر</option><option>همهٔ بازه‌ها</option></select></label>
      <label>استاندارد خدمت<select defaultValue="همه"><option>همه</option><option>در محدوده</option></select></label>
      <button className="primary-button" onClick={() => setFilters(draft)}>اعمال فیلتر</button>
    </div></section>
      <div className="delivery-list-columns"><section className="panel delivery-list-table-panel"><div className="panel-heading"><div><span className="eyebrow">فهرست نمایشی</span><h2>نتایج ارسال</h2></div><span className="panel-meta">{fa.format(visibleItems.length)} مورد نمونه</span></div>
      <div className="merchant-delivery-table-wrap"><table className="merchant-delivery-table demo-merchant-delivery-table"><thead><tr><th>عملیات</th><th>شماره سفارش</th><th>پیک / مأموریت</th><th>گیرنده</th><th>شعبه</th><th>تعهد</th><th>مبلغ</th><th>وضعیت</th></tr></thead><tbody>{visibleItems.map((item,index) => <tr key={item.order}><td><button className="history-button" onClick={() => onSelect(item)}>جزئیات</button></td><td dir="ltr">{item.order}</td><td><span dir="ltr">{item.mission}</span><small className="block">پیک نمونه</small></td><td>{item.recipient}</td><td>{item.branch}</td><td>{item.deadline}</td><td>{index % 2 ? '۲۷۰٬۰۰۰ تومان' : '۲۴۰٬۰۰۰ تومان'}</td><td><span className={`badge ${item.status === 'تحویل' ? 'success' : item.status === 'نیازمند اقدام' ? 'danger' : 'info'}`}>{item.status}</span></td></tr>)}</tbody></table>{!visibleItems.length && <div className="empty-state"><strong>ارسالی پیدا نشد</strong><small>جست‌وجو فقط بین ردیف‌های نمونه انجام می‌شود.</small></div>}</div>
      <p className="data-note">خروجی و فیلترهای عملیاتی از API واقعی استفاده می‌کنند؛ اینجا فقط تعامل نمایشی است.</p>
    </section><aside className="delivery-list-side"><section className="panel delivery-list-summary"><h2>خلاصهٔ فیلتر</h2><DemoSummary label="جست‌وجو" value={filters.query || 'همهٔ موارد نمونه'} /><DemoSummary label="وضعیت" value={filters.status || 'همهٔ وضعیت‌ها'} /><DemoSummary label="شعبه" value={filters.branch || 'همهٔ شعب'} /><DemoSummary label="نتیجه" value={`${fa.format(visibleItems.length)} ردیف نمونه`} /><button className="secondary-button" onClick={() => { const empty = { query: '', status: '', branch: '' }; setDraft(empty); setFilters(empty) }}>پاک‌کردن فیلترها</button></section>
      <section className="panel delivery-list-exceptions"><div className="panel-heading"><h2>موارد نیازمند اقدام</h2><span className="badge danger">۲ نمونه</span></div><p>گیرنده در دسترس نیست · ORD-۰۳۷۱</p><p>تأخیر آماده‌سازی · ORD-۰۳۵۴</p></section>
      <section className="panel delivery-list-saved-view"><h2>نمای ذخیره‌شده</h2><p>ارسال‌های فعال امروز · نمونه</p><button className="secondary-button" disabled>اعمال نمای نمونه</button></section>
    </aside></div>
  </main>
}

function DetailMetric({ label, value }: { label: string; value: string }) {
  return <article><small>{label}</small><strong>{value}</strong></article>
}

function DemoSummary({ label, value }: { label: string; value: string }) {
  return <div className="create-summary-line"><span>{label}</span><strong>{value}</strong></div>
}

function DemoBranchDrawer({ onClose, onSave }: { onClose: () => void; onSave: () => void }) {
  return <div className="demo-drawer-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}><section className="demo-drawer" role="dialog" aria-modal="true" aria-labelledby="branch-drawer-title" dir="rtl"><header><div><small>مدیریت شعب</small><h2 id="branch-drawer-title">افزودن شعبه</h2></div><button type="button" aria-label="بستن" onClick={onClose}>×</button></header><form onSubmit={event => { event.preventDefault(); onSave() }}><label>نام شعبه<input required defaultValue="شعبه جدید" /></label><label>مسئول شعبه<input required placeholder="نام مسئول" /></label><label>شماره تماس<input required inputMode="tel" placeholder="۰۲۱ ..." /></label><label>آدرس<input required placeholder="تهران، خیابان، پلاک" /></label><div className="demo-drawer-grid"><label>ساعت شروع<input type="time" defaultValue="08:00" /></label><label>ساعت پایان<input type="time" defaultValue="22:00" /></label></div><label>شعاع سرویس<select defaultValue="5"><option value="3">۳ کیلومتر</option><option value="5">۵ کیلومتر</option><option value="8">۸ کیلومتر</option></select></label><label className="demo-drawer-check"><input type="checkbox" defaultChecked />آمادهٔ پذیرش ارسال</label><footer><button type="button" className="secondary-button" onClick={onClose}>انصراف</button><button className="primary-button">ثبت شعبه</button></footer></form></section></div>
}

function DemoInviteDrawer({ onClose, onSave }: { onClose: () => void; onSave: () => void }) {
  return <div className="demo-drawer-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}><section className="demo-drawer" role="dialog" aria-modal="true" aria-labelledby="invite-drawer-title" dir="rtl"><header><div><small>اعضا و دسترسی</small><h2 id="invite-drawer-title">دعوت عضو جدید</h2></div><button type="button" aria-label="بستن" onClick={onClose}>×</button></header><form onSubmit={event => { event.preventDefault(); onSave() }}><label>ایمیل یا شماره موبایل<input required placeholder="name@example.com یا ۰۹۱۲..." /></label><label>نقش<select defaultValue="operator"><option value="admin">ادمین</option><option value="operator">اپراتور</option><option value="finance">مالی</option><option value="viewer">مشاهده‌گر</option></select></label><label>محدودهٔ شعب<select defaultValue="all"><option value="all">همهٔ شعب</option><option value="vanak">ونک</option><option value="jordan">جردن</option></select></label><p className="data-note">این دعوت فقط در پیش‌نمایش طراحی ثبت می‌شود و پیام واقعی ارسال نمی‌کند.</p><footer><button type="button" className="secondary-button" onClick={onClose}>انصراف</button><button className="primary-button">ثبت دعوت</button></footer></form></section></div>
}

function DemoWebhookDrawer({ onClose, onSave }: { onClose: () => void; onSave: () => void }) {
  return <div className="demo-drawer-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}><section className="demo-drawer" role="dialog" aria-modal="true" aria-labelledby="webhook-drawer-title" dir="rtl"><header><div><small>Webhook و رویدادهای خروجی</small><h2 id="webhook-drawer-title">افزودن Webhook Endpoint</h2></div><button type="button" aria-label="بستن" onClick={onClose}>×</button></header><form onSubmit={event => { event.preventDefault(); onSave() }}><label>نام Endpoint<input required defaultValue="ERP اصلی" /></label><label>محیط<select defaultValue="production"><option value="production">Production</option><option value="staging">Staging</option></select></label><label>URL مقصد<input type="url" required placeholder="https://example.com/webhooks/mono" /></label><label>Webhook Secret<input required placeholder="Secret را وارد کنید" /></label><fieldset className="demo-webhook-events"><legend>رویدادهای موردنیاز</legend>{['mission.updated','delivery.completed','incident.created','billing.created'].map((event,index)=><label key={event}><input type="checkbox" defaultChecked={index<3} />{event}</label>)}</fieldset><section className="panel demo-webhook-retry"><strong>Retry Policy</strong><p>۵ تلاش · Backoff نمایی · ۲۴ ساعت</p><label><input type="checkbox" defaultChecked />قبل از فعال‌سازی Endpoint را تست کن</label></section><label>توضیح داخلی<input defaultValue="اتصال اصلی ERP سپهر" /></label><p className="data-note">ثبت این فرم فقط در پیش‌نمایش انجام می‌شود و Endpoint واقعی ساخته نمی‌شود.</p><footer><button type="button" className="secondary-button" onClick={onClose}>انصراف</button><button className="primary-button">ذخیره Endpoint</button></footer></form></section></div>
}
