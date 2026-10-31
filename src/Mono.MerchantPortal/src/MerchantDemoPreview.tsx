import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import newDeliveryIcon from './assets/figma-dashboard/icon-1.svg'
import trackingIcon from './assets/figma-dashboard/icon-2.svg'
import branchesIcon from './assets/figma-dashboard/icon-3.svg'
import billingIcon from './assets/figma-dashboard/icon-4.svg'
import settingsIcon from './assets/figma-dashboard/icon-5.svg'

type PreviewView = 'dashboard' | 'deliveries' | 'create' | 'tracking' | 'branches' | 'billing' | 'settings'
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
]

const menu: Array<{ id: PreviewView; label: string; icon: string | null }> = [
  { id: 'dashboard', label: 'داشبورد', icon: null }, { id: 'create', label: 'ارسال جدید', icon: newDeliveryIcon },
  { id: 'deliveries', label: 'ارسال‌ها', icon: null }, { id: 'tracking', label: 'رهگیری', icon: trackingIcon },
  { id: 'branches', label: 'شعب', icon: branchesIcon }, { id: 'billing', label: 'صورتحساب', icon: billingIcon },
  { id: 'settings', label: 'تنظیمات', icon: settingsIcon },
]

export default function MerchantDemoPreview({ onExit }: { onExit: () => void }) {
  const [view, setView] = useState<PreviewView>('dashboard')
  const [deliveries, setDeliveries] = useState(initialDeliveries)
  const [tracking, setTracking] = useState('ORD-۰۳۸۴')
  const [trackingResult, setTrackingResult] = useState<PreviewDelivery | null>(initialDeliveries[0])
  const [notice, setNotice] = useState('')
  const title: Record<PreviewView, string> = {
    dashboard: 'داشبورد کسب‌وکار', deliveries: 'ارسال‌های شما', create: 'ثبت ارسال جدید',
    tracking: 'رهگیری ارسال', branches: 'شعب کسب‌وکار', billing: 'صورتحساب', settings: 'تنظیمات Workspace',
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
      <nav className="navigation">{menu.map((item, index) => <button key={`${item.id}-${index}`} className={`nav-item ${view === item.id ? 'active' : ''}`} onClick={() => { setView(item.id); setNotice('') }}><span className={`nav-icon ${item.id === 'dashboard' ? 'dashboard-icon' : item.id === 'deliveries' ? 'deliveries-icon' : ''}`} aria-hidden="true">{item.icon ? <img src={item.icon} alt="" /> : item.id === 'dashboard' ? <><i /><i /><i /><i /></> : <i />}</span>{item.label}</button>)}</nav>
      <div className="sidebar-footer demo-account"><div><strong>مدیر حساب سپهر</strong><small>Workspace فروشگاه سپهر</small><small className="demo-label sr-only">محیط نمایشی · داده‌های ساختگی</small></div><button className="sidebar-logout" onClick={onExit}>خروج</button></div>
    </aside>

    <main className="workspace">
      <header className="topbar"><div className="topbar-actions"><button className="secondary-button topbar-action" onClick={() => setView('tracking')}>رهگیری سریع</button><button className="primary-button topbar-action" onClick={() => setView('create')}>ارسال جدید</button></div><div className="page-title"><h1>{title[view]}</h1><p>{view === 'dashboard' ? 'فروشگاه سپهر • مدیریت ارسال‌های شهری و شعب' : 'فروشگاه سپهر • محیط نمایشی'}</p></div></header>
      {notice && <div className="message success" role="status">{notice}</div>}

      {view === 'dashboard' && <>
        <section className="demo-kpi-strip" aria-label="شاخص‌های نمایشی"><DemoKpi label="ارسال فعال" value={fa.format(activeCount)} /><DemoKpi label="نیازمند اقدام" value="۲" tone="red" /><DemoKpi label="تحویل امروز" value="۵۸" tone="green" /><DemoKpi label="شعب فعال" value="۳" /><DemoKpi label="میانگین تحویل" value="۲۶ دقیقه" /></section>
        <div className="merchant-dashboard">
        <div className="dashboard-main-column">
          <section className="panel demo-active-panel"><div className="active-panel-heading"><span className="badge">{fa.format(activeCount)} فعال</span><h2>ارسال‌های فعال</h2></div><PreviewTable items={deliveries.slice(0, 7)} onSelect={item => { setTrackingResult(item); setView('tracking') }} /></section>
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

      {view === 'deliveries' && <section className="panel"><div className="panel-heading"><div><span className="eyebrow">فهرست نمایشی</span><h2>ارسال‌ها</h2></div><button className="primary-button" onClick={() => setView('create')}>＋ ارسال جدید</button></div><PreviewTable items={deliveries} onSelect={item => { setTrackingResult(item); setView('tracking') }} /></section>}
      {view === 'create' && <section className="panel form-panel"><div className="panel-heading"><div><span className="eyebrow">مرحلهٔ ۱ از ۲</span><h2>اطلاعات ارسال</h2></div><span className="panel-meta">فرم نمایشی</span></div><form className="delivery-form demo-create-form" onSubmit={createSample}><div className="form-grid two"><label>شمارهٔ سفارش<input name="order" required defaultValue="ORD-۰۳۹۰" /></label><label>شعبهٔ مبدأ<select defaultValue="ونک"><option>ونک</option><option>جردن</option><option>میرداماد</option></select></label><label className="wide">نشانی مبدأ<input required defaultValue="تهران، خیابان ولیعصر" /></label><label className="wide">نشانی گیرنده<input required defaultValue="تهران، میدان ونک" /></label><label>نام گیرنده<input name="recipient" defaultValue="مشتری نمونه" /></label><label>تلفن گیرنده<input inputMode="tel" defaultValue="۰۹۱۲۰۰۰۰۰۰۰" /></label><label>تعداد بسته<input type="number" min="1" defaultValue="1" /></label></div><p className="form-note">این ثبت فقط در حافظهٔ پیش‌نمایش انجام می‌شود.</p><button className="primary-button">ثبت نمونهٔ ارسال</button></form></section>}
      {view === 'tracking' && <section className="panel demo-tracking"><div className="panel-heading"><div><span className="eyebrow">رهگیری</span><h2>وضعیت ارسال</h2></div></div><form className="quick-track-form" onSubmit={event => { event.preventDefault(); setTrackingResult(deliveries.find(item => item.order === tracking || item.mission === tracking) || null) }}><input aria-label="شناسهٔ ارسال یا مأموریت" value={tracking} onChange={event => setTracking(event.target.value)} /><button className="primary-button">جست‌وجو</button></form>{trackingResult ? <article className="demo-track-result"><strong>{trackingResult.order}</strong><span className="badge success">{trackingResult.status}</span><p>گیرنده: {trackingResult.recipient} · شعبه: {trackingResult.branch}</p><p>تعهد تحویل: {trackingResult.deadline} · مأموریت: {trackingResult.mission}</p><div className="demo-timeline"><span>ثبت درخواست</span><i /><span>پذیرش پیک</span><i /><span>تحویل</span></div></article> : <div className="empty-state"><strong>ارسالی با این شناسه پیدا نشد</strong><small>در این محیط فقط شناسه‌های نمونه قابل جست‌وجو هستند.</small></div>}</section>}
      {view === 'branches' && <section className="panel"><div className="panel-heading"><div><span className="eyebrow">سه شعبهٔ نمونه</span><h2>شعب فعال</h2></div></div><div className="branch-grid">{[['ونک','تهران، خیابان ملاصدرا'],['جردن','تهران، بلوار نلسون ماندلا'],['میرداماد','تهران، خیابان میرداماد']].map(([name,address]) => <article className="branch-card" key={name}><span className="branch-symbol">⌂</span><div><strong>{name}</strong><p>{address}</p><small>زون شهری تهران</small></div><span className="badge success">فعال</span></article>)}</div><p className="data-note">مدیریت واقعی شعب به API ایجاد و ویرایش شعبه نیاز دارد.</p></section>}
      {view === 'billing' && <section className="panel demo-blocked"><span className="eyebrow">صورتحساب</span><h2>اطلاعات مالی نمایشی نیست</h2><p>تا اتصال دفترکل و قرارداد صورتحساب، مبلغ یا ماندهٔ ساختگی نشان نمی‌دهیم. این صفحه در نسخهٔ عملیاتی فقط با API مالی معتبر فعال می‌شود.</p></section>}
      {view === 'settings' && <section className="panel settings-panel"><div className="panel-heading"><div><span className="eyebrow">حساب کسب‌وکار</span><h2>تنظیمات</h2></div></div><div className="settings-tabs">{['یکپارچه‌سازی','اعلان‌ها','اعضا و دسترسی','Workspace','هویت بصری'].map(label => <button key={label} onClick={() => setNotice(`بخش «${label}» در پیش‌نمایش فقط برای مرور چیدمان است.`)}>{label}</button>)}</div><div className="settings-content"><div className="empty-state"><strong>تنظیمات نمایشی</strong><small>مدیریت اعضا، اعلان‌ها، برند و Workspace تا آماده‌شدن APIهای حساب غیرفعال می‌ماند.</small></div><button className="secondary-button" onClick={() => setView('deliveries')}>بازگشت به ارسال‌ها</button></div></section>}
    </main>
  </div>
}

function DemoKpi({ label, value, tone = 'cyan' }: { label: string; value: string; tone?: string }) {
  return <article className={`demo-kpi ${tone}`}><small>{label}</small><strong>{value}</strong></article>
}

function PreviewTable({ items, onSelect }: { items: PreviewDelivery[]; onSelect: (item: PreviewDelivery) => void }) {
  return <div className="table-wrap"><table className="demo-delivery-table"><thead><tr><th>وضعیت</th><th>گیرنده</th><th>شعبه</th><th>تعهد تحویل</th><th>مأموریت</th><th>سفارش</th></tr></thead><tbody>{items.map(item => <tr key={item.order} onClick={() => onSelect(item)} tabIndex={0} onKeyDown={event => { if (event.key === 'Enter') onSelect(item) }}><td><span className={`badge ${item.status === 'تحویل' ? 'success' : item.status === 'دریافت' ? 'received' : item.status === 'نیازمند اقدام' ? 'danger' : 'info'}`}>{item.status}</span></td><td>{item.recipient}</td><td>{item.branch}</td><td>{item.deadline}</td><td dir="ltr">{item.mission}</td><td dir="ltr">{item.order}</td></tr>)}</tbody></table></div>
}
