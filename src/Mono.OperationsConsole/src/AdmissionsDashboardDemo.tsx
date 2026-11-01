import { useMemo, useState } from 'react'
import './admissions-dashboard-demo.css'

type Props = { onNavigate: (view: 'overview' | 'couriers' | 'onboarding') => void }
type Application = { type: 'پیک' | 'کسب‌وکار'; id: string; name: string; stage: string; lastAction: string; status: 'در بررسی' | 'نیازمند اقدام' | 'ناقص' }

const applications: Application[] = [
  { type: 'پیک', id: 'CR-REG-۰۱۴۸', name: 'رضا احمدی', stage: 'بررسی مدارک', lastAction: '۸ دقیقه پیش', status: 'در بررسی' },
  { type: 'کسب‌وکار', id: 'BIZ-REG-۰۰۳۴', name: 'فروشگاه سپهر', stage: 'اطلاعات حقوقی', lastAction: '۱۲ دقیقه پیش', status: 'در بررسی' },
  { type: 'پیک', id: 'CR-REG-۰۱۴۲', name: 'سعید مرادی', stage: 'احراز هویت', lastAction: '۱۵ دقیقه پیش', status: 'نیازمند اقدام' },
  { type: 'پیک', id: 'CR-REG-۰۱۳۹', name: 'مجتبی کریمی', stage: 'اعتماد و تضمین', lastAction: '۲۱ دقیقه پیش', status: 'در بررسی' },
  { type: 'کسب‌وکار', id: 'BIZ-REG-۰۰۳۱', name: 'سوپرمارکت آریا', stage: 'مدارک', lastAction: '۲۶ دقیقه پیش', status: 'ناقص' },
  { type: 'پیک', id: 'CR-REG-۰۱۳۴', name: 'امیر رضایی', stage: 'مناطق و شیوه حمل', lastAction: '۳۳ دقیقه پیش', status: 'در بررسی' },
]

const pipeline = [['۳۴', 'ثبت‌نام‌شده'], ['۲۱', 'در حال احراز'], ['۱۴', 'مدارک ناقص'], ['۱۱', 'نیازمند بررسی'], ['۹', 'آماده فعال‌سازی'], ['۳', 'ردشده']]
const today = [
  ['بحرانی', 'CR-REG-۰۱۴۲', 'پیک • احراز هویت ناموفق'], ['بالا', 'BIZ-REG-۰۰۳۱', 'کسب‌وکار • مدارک حقوقی ناقص'],
  ['بالا', 'CR-REG-۰۱۳۴', 'پیک • ضمانت نیازمند بررسی'], ['متوسط', 'BIZ-REG-۰۰۲۷', 'کسب‌وکار • قرارداد بدون امضا'], ['متوسط', 'CR-REG-۰۱۲۸', 'پیک • شبا نامعتبر'],
]
const courierStages = ['ثبت‌نام', 'احراز هویت', 'اطلاعات بانکی', 'شیوه حمل و وسیله', 'مناطق', 'اعتماد و تضمین', 'تأیید مونو', 'فعال']
const businessStages = ['ثبت‌نام', 'اطلاعات حقوقی', 'شعب', 'سرویس', 'قرارداد و مالی', 'روش اتصال', 'تأیید مونو', 'فعال']

export default function AdmissionsDashboardDemo({ onNavigate }: Props) {
  const [tab, setTab] = useState<'همه' | 'پیک‌ها' | 'کسب‌وکارها'>('همه')
  const [section, setSection] = useState('داشبورد')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Application | null>(null)
  const [showCreate, setShowCreate] = useState(false)
  const [toast, setToast] = useState('')
  const filtered = useMemo(() => applications.filter(item => {
    const typeMatches = tab === 'همه' || (tab === 'پیک‌ها' ? item.type === 'پیک' : item.type === 'کسب‌وکار')
    const sectionMatches = section === 'مدارک' ? item.status === 'ناقص' : section === 'فعال‌سازی' ? item.stage === 'اعتماد و تضمین' : section === 'ردشده‌ها' ? item.status === 'نیازمند اقدام' : true
    const q = search.trim().toLocaleLowerCase('fa-IR')
    return typeMatches && sectionMatches && (!q || `${item.id} ${item.name} ${item.stage} ${item.type}`.toLocaleLowerCase('fa-IR').includes(q))
  }), [tab, search, section])
  function notify(message: string) { setToast(message); window.setTimeout(() => setToast(''), 2600) }

  return <main className="admissions-demo" dir="rtl">
    <aside className="admissions-demo-sidebar">
      <div className="admissions-demo-brand"><span className="admissions-demo-mark" aria-hidden="true"><i /><b>↔</b></span><span><strong>مونو</strong><small>لجستیک هوشمند</small></span></div>
      <nav aria-label="منوی پذیرش">
        {(['داشبورد', 'پیک‌ها', 'کسب‌وکارها', 'احراز و مدارک', 'فعال‌سازی', 'ردشده‌ها'] as const).map((item, index) => <button key={item} className={section === item ? 'active' : ''} onClick={() => {
          setSection(item)
          if (item === 'داشبورد') onNavigate('overview')
          if (item === 'پیک‌ها') { setTab('پیک‌ها'); onNavigate('couriers') }
          if (item === 'کسب‌وکارها') { setTab('کسب‌وکارها'); onNavigate('onboarding') }
          if (item === 'احراز و مدارک') { setTab('همه'); setSection('مدارک') }
        }}><span className="admissions-demo-navicon">{['▦', '♧', '♙', '▤', '♧', '⊘'][index]}</span>{item}</button>)}
      </nav>
      <div className="admissions-demo-profile"><span className="admissions-demo-avatar">م</span><span><strong>کارشناس پذیرش مونو</strong><small>تیم پذیرش و احراز</small></span></div>
      <button className="admissions-demo-logout" onClick={() => notify('برای خروج از اتصال API استفاده کن.')}>خروج</button>
    </aside>

    <div className="admissions-demo-workspace">
      <header className="admissions-demo-header">
        <div className="admissions-demo-header-actions"><button className="admissions-demo-secondary" onClick={() => notify('معیارهای پذیرش در نمای نمایشی قابل تغییر نیستند.')}>تنظیم معیارها</button><button className="admissions-demo-primary" onClick={() => setShowCreate(true)}>ثبت درخواست</button></div>
        <div><h1>ثبت‌نام و پذیرش مونو</h1><p>پذیرش پیک‌ها و کسب‌وکارها، بررسی مدارک، احراز، قرارداد و فعال‌سازی</p></div>
      </header>
      <section className="admissions-demo-kpis" aria-label="آمار پذیرش">
        {[["درخواست جدید", '۳۴'], ['پیک در بررسی', '۲۸'], ['کسب‌وکار در بررسی', '۶'], ['مدارک ناقص', '۱۴'], ['آماده فعال‌سازی', '۹']].map(([label, value]) => <article key={label}><small>{label}</small><strong>{value}</strong></article>)}
      </section>
      <div className="admissions-demo-tabs" role="tablist" aria-label="نوع پرونده">
        {(['همه', 'پیک‌ها', 'کسب‌وکارها'] as const).map(item => <button key={item} role="tab" aria-selected={tab === item} className={tab === item ? 'active' : ''} onClick={() => setTab(item)}>{item}</button>)}
      </div>

      <div className="admissions-demo-content">
        <aside className="admissions-demo-left">
          <section className="admissions-demo-card pipeline-card"><h2>وضعیت پایپ‌لاین</h2><div>{pipeline.map(([value, label]) => <button key={label} onClick={() => { setSection(label === 'مدارک ناقص' ? 'مدارک' : label === 'آماده فعال‌سازی' ? 'فعال‌سازی' : label === 'ردشده' ? 'ردشده‌ها' : 'داشبورد'); if (label === 'مدارک ناقص') setTab('همه') }}><strong>{value}</strong><span>{label}</span></button>)}</div></section>
          <section className="admissions-demo-card action-today"><h2>نیازمند اقدام امروز</h2><div>{today.map(([priority, id, desc]) => <button key={id} onClick={() => setSelected(applications.find(x => x.id === id) || { type: id.startsWith('BIZ') ? 'کسب‌وکار' : 'پیک', id, name: desc.split(' • ')[1], stage: desc.split(' • ')[1], lastAction: 'امروز', status: 'نیازمند اقدام' })}><span className={`priority priority-${priority}`}>{priority}</span><div><strong>{id}</strong><small>{desc}</small></div></button>)}</div></section>
        </aside>

        <div className="admissions-demo-main">
          <section className="admissions-demo-card admission-queue">
            <header><div><h2>صف ثبت‌نام و پذیرش</h2><span>۳۴ پرونده</span></div><label><input aria-label="جست‌وجوی شناسه، نام، موبایل یا کسب‌وکار" placeholder="جست‌وجوی شناسه، نام، موبایل یا کسب‌وکار" value={search} onChange={e => setSearch(e.target.value)} /><span>⌕</span></label></header>
            <div className="admissions-demo-table-wrap"><table><thead><tr>{['نوع', 'شناسه', 'نام', 'مرحله', 'آخرین اقدام', 'وضعیت'].map(x => <th key={x}>{x}</th>)}</tr></thead><tbody>{filtered.map(item => <tr key={item.id} onClick={() => setSelected(item)}><td>{item.type}</td><td><bdi dir="ltr">{item.id}</bdi></td><td>{item.name}</td><td>{item.stage}</td><td>{item.lastAction}</td><td><span className={`admission-status status-${item.status === 'ناقص' ? 'incomplete' : item.status === 'نیازمند اقدام' ? 'action' : 'review'}`}>{item.status}</span></td></tr>)}{filtered.length === 0 && <tr><td colSpan={6} className="admission-empty-row">موردی برای این جست‌وجو پیدا نشد.</td></tr>}</tbody></table></div>
            <footer><span>{filtered.length} پرونده از ۳۴ پروندهٔ نمونه</span><button disabled>قبلی</button><b>۱</b><button disabled>بعدی</button></footer>
          </section>
          <div className="admissions-demo-processes"><ProcessCard title="مسیر پذیرش پیک" count="۲۸ پرونده" stages={courierStages} /><ProcessCard title="مسیر پذیرش کسب‌وکار" count="۶ پرونده" stages={businessStages} /></div>
        </div>
      </div>
    </div>
    {toast && <div role="status" className="admissions-demo-toast">{toast}</div>}
    {showCreate && <div className="admissions-demo-backdrop" onClick={() => setShowCreate(false)}><section role="dialog" aria-modal="true" aria-labelledby="admission-create-title" onClick={e => e.stopPropagation()}><button className="admissions-demo-close" onClick={() => setShowCreate(false)} aria-label="بستن">×</button><h2 id="admission-create-title">ثبت درخواست پذیرش</h2><p>در نسخهٔ نمایشی، درخواست جدید به سامانه ثبت نمی‌شود.</p><label>نوع درخواست<select defaultValue="courier"><option value="courier">پیک</option><option value="business">کسب‌وکار</option></select></label><button className="admissions-demo-primary" onClick={() => { setShowCreate(false); notify('این پیش‌نمایش دادهٔ نمایشی است و درخواستی ثبت نشد.') }}>ادامه</button></section></div>}
    {selected && <div className="admissions-demo-backdrop" onClick={() => setSelected(null)}><section role="dialog" aria-modal="true" aria-labelledby="admission-detail-title" onClick={e => e.stopPropagation()}><button className="admissions-demo-close" onClick={() => setSelected(null)} aria-label="بستن">×</button><h2 id="admission-detail-title">پروندهٔ {selected.id}</h2><dl><div><dt>نوع</dt><dd>{selected.type}</dd></div><div><dt>نام</dt><dd>{selected.name}</dd></div><div><dt>مرحله</dt><dd>{selected.stage}</dd></div><div><dt>وضعیت</dt><dd>{selected.status}</dd></div><div><dt>آخرین اقدام</dt><dd>{selected.lastAction}</dd></div></dl><button className="admissions-demo-primary" onClick={() => { setSelected(null); notify('جزئیات کامل در اتصال عملیاتی API نمایش داده می‌شود.') }}>باز کردن پرونده</button></section></div>}
  </main>
}

function ProcessCard({ title, count, stages }: { title: string; count: string; stages: string[] }) {
  return <section className="admissions-demo-card process-card"><header><h2>{title}</h2><span>{count}</span></header><div>{stages.map((stage, i) => <span key={stage} className={i < 3 ? 'complete' : i === 3 ? 'current' : ''}>{stage}</span>)}</div></section>
}
