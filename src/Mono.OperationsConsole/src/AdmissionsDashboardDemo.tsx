import { useMemo, useState } from 'react'
import './admissions-dashboard-demo.css'

type Props = { onNavigate: (view: 'overview' | 'couriers' | 'onboarding') => void; onSectionChange?: (section: string) => void }
type Application = { type: 'پیک' | 'کسب‌وکار'; id: string; name: string; stage: string; lastAction: string; status: 'در بررسی' | 'نیازمند اقدام' | 'ناقص' | 'ردشده' }

const applications: Application[] = [
  { type: 'پیک', id: 'CR-REG-۰۱۴۸', name: 'رضا احمدی', stage: 'بررسی مدارک', lastAction: '۸ دقیقه پیش', status: 'در بررسی' },
  { type: 'کسب‌وکار', id: 'BIZ-REG-۰۰۳۴', name: 'فروشگاه سپهر', stage: 'اطلاعات حقوقی', lastAction: '۱۲ دقیقه پیش', status: 'در بررسی' },
  { type: 'پیک', id: 'CR-REG-۰۱۴۲', name: 'سعید مرادی', stage: 'احراز هویت', lastAction: '۱۵ دقیقه پیش', status: 'نیازمند اقدام' },
  { type: 'پیک', id: 'CR-REG-۰۱۳۹', name: 'مجتبی کریمی', stage: 'اعتماد و تضمین', lastAction: '۲۱ دقیقه پیش', status: 'در بررسی' },
  { type: 'کسب‌وکار', id: 'BIZ-REG-۰۰۳۱', name: 'سوپرمارکت آریا', stage: 'مدارک', lastAction: '۲۶ دقیقه پیش', status: 'ناقص' },
  { type: 'پیک', id: 'CR-REG-۰۱۳۴', name: 'امیر رضایی', stage: 'مناطق و شیوه حمل', lastAction: '۳۳ دقیقه پیش', status: 'در بررسی' },
]
const rejectedApplications: Application[] = [
  { type: 'پیک', id: 'CR-REG-۰۱۲۴', name: 'حامد کریمی', stage: 'احراز هویت', lastAction: 'دیروز', status: 'ردشده' },
  { type: 'کسب‌وکار', id: 'BIZ-REG-۰۰۲۲', name: 'فروشگاه نمونه', stage: 'اطلاعات حقوقی', lastAction: '۲ روز پیش', status: 'ردشده' },
  { type: 'پیک', id: 'CR-REG-۰۱۱۹', name: 'میلاد حیدری', stage: 'اعتماد و تضمین', lastAction: '۳ روز پیش', status: 'ردشده' },
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
  const filtered = useMemo(() => (section === 'ردشده‌ها' ? rejectedApplications : applications).filter(item => {
    const typeMatches = tab === 'همه' || (tab === 'پیک‌ها' ? item.type === 'پیک' : item.type === 'کسب‌وکار')
    const sectionMatches = section === 'مدارک' ? item.status === 'ناقص' : section === 'فعال‌سازی' ? item.stage === 'اعتماد و تضمین' : true
    const q = search.trim().toLocaleLowerCase('fa-IR')
    return typeMatches && sectionMatches && (!q || `${item.id} ${item.name} ${item.stage} ${item.type}`.toLocaleLowerCase('fa-IR').includes(q))
  }), [tab, search, section])
  function notify(message: string) { setToast(message); window.setTimeout(() => setToast(''), 2600) }

  if (section === 'پیک‌ها') return <CourierAdmissionPage onNavigate={onNavigate} onSectionChange={setSection} />
  if (section === 'کسب‌وکارها') return <BusinessAdmissionPage onNavigate={onNavigate} onSectionChange={setSection} />
  if (section === 'مدارک') return <VerificationDocumentsPage onNavigate={onNavigate} onSectionChange={setSection} />
  if (section === 'فعال‌سازی') return <ActivationPage onNavigate={onNavigate} onSectionChange={setSection} />

  return <main className="admissions-demo" dir="rtl">
    <aside className="admissions-demo-sidebar">
      <div className="admissions-demo-brand"><span className="admissions-demo-mark" aria-hidden="true"><i /><b>↔</b></span><span><strong>مونو</strong><small>لجستیک هوشمند</small></span></div>
      <nav aria-label="منوی پذیرش">
        {(['داشبورد', 'پیک‌ها', 'کسب‌وکارها', 'احراز و مدارک', 'فعال‌سازی', 'ردشده‌ها'] as const).map((item, index) => <button key={item} className={section === item || (item === 'احراز و مدارک' && section === 'مدارک') ? 'active' : ''} onClick={() => {
          setSection(item)
          if (item === 'داشبورد') onNavigate('overview')
          if (item === 'پیک‌ها') setTab('پیک‌ها')
          if (item === 'کسب‌وکارها') setTab('کسب‌وکارها')
          if (item === 'احراز و مدارک') { setTab('همه'); setSection('مدارک') }
        }}><span className="admissions-demo-navicon"><AdmissionNavIcon index={index} /></span>{item}</button>)}
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
            <div className="admissions-demo-table-wrap"><table><thead><tr>{['نوع', 'شناسه', 'نام', 'مرحله', 'آخرین اقدام', 'وضعیت'].map(x => <th key={x}>{x}</th>)}</tr></thead><tbody>{filtered.map(item => <tr key={item.id} onClick={() => setSelected(item)}><td>{item.type}</td><td><bdi dir="ltr">{item.id}</bdi></td><td>{item.name}</td><td>{item.stage}</td><td>{item.lastAction}</td><td><span className={`admission-status status-${item.status === 'ناقص' ? 'incomplete' : item.status === 'نیازمند اقدام' ? 'action' : item.status === 'ردشده' ? 'rejected' : 'review'}`}>{item.status}</span></td></tr>)}{filtered.length === 0 && <tr><td colSpan={6} className="admission-empty-row">موردی برای این جست‌وجو پیدا نشد.</td></tr>}</tbody></table></div>
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

function AdmissionNavIcon({ index }: { index: number }) {
  const common = { width: 15, height: 15, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 1.7, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, 'aria-hidden': true as const }
  if (index === 0) return <svg {...common}><rect x="4" y="4" width="6" height="6" rx="1"/><rect x="14" y="4" width="6" height="6" rx="1"/><rect x="4" y="14" width="6" height="6" rx="1"/><rect x="14" y="14" width="6" height="6" rx="1"/></svg>
  if (index === 1) return <svg {...common}><circle cx="8" cy="17" r="3"/><circle cx="18" cy="17" r="3"/><path d="m8 17 4-8h4l3 8M12 9l-2-3H7"/></svg>
  if (index === 2) return <svg {...common}><circle cx="9" cy="8" r="3"/><path d="M3 20v-1a6 6 0 0 1 12 0v1m2-12a3 3 0 0 1 3 3m-1 3a5 5 0 0 1 3 5v1"/></svg>
  if (index === 3) return <svg {...common}><path d="M6 3h8l4 4v14H6zM14 3v5h5M9 13h6m-6 4h6"/></svg>
  if (index === 4) return <svg {...common}><circle cx="12" cy="12" r="8"/><path d="m8 12 2.5 2.5L16 9"/></svg>
  return <svg {...common}><circle cx="12" cy="12" r="8"/><path d="m9 9 6 6m0-6-6 6"/></svg>
}

const courierCases = [
  { id: 'CR-REG-۰۱۴۸', name: 'رضا احمدی', detail: 'بررسی مدارک', age: '۸ دقیقه پیش', flag: 'بررسی', tone: 'review' },
  { id: 'CR-REG-۰۱۴۲', name: 'سعید مرادی', detail: 'احراز هویت', age: '۱۵ دقیقه پیش', flag: 'اقدام', tone: 'action' },
  { id: 'CR-REG-۰۱۳۹', name: 'مجتبی کریمی', detail: 'اعتماد و تضمین', age: '۲۱ دقیقه پیش', flag: 'بررسی', tone: 'review' },
  { id: 'CR-REG-۰۱۳۴', name: 'امیر رضایی', detail: 'مناطق و وسیله', age: '۳۳ دقیقه پیش', flag: 'بررسی', tone: 'review' },
  { id: 'CR-REG-۰۱۲۸', name: 'حسین کریمی', detail: 'اطلاعات بانکی', age: '۴۷ دقیقه پیش', flag: 'اقدام', tone: 'action' },
  { id: 'CR-REG-۰۱۲۲', name: 'علی موسوی', detail: 'تأیید نهایی', age: '۵۸ دقیقه پیش', flag: 'آماده', tone: 'ready' },
]
const courierChecks = [['احراز هویت', 'تأیید', 'امروز ۰۹:۴۸', 'done'], ['اطلاعات بانکی', 'تأیید', 'امروز ۰۹:۵۱', 'done'], ['کارت وسیله', 'تأیید', 'امروز ۱۰:۰۲', 'done'], ['بیمه وسیله', 'در بررسی', 'انقضا ۱۴۰۶/۰۹', 'pending'], ['تصویر وسیله', 'تأیید', 'امروز ۱۰:۰۴', 'done'], ['سفته الکترونیک', 'تأیید', 'ثبت‌شده', 'done'], ['اعتماد / تضمین', 'پذیرفته', 'نسخه ۱.۰', 'done'], ['قوانین مونو', 'تأیید', 'نسخه ۱.۰', 'done']]

function CourierAdmissionPage({ onNavigate, onSectionChange }: Props) {
  const [selectedId, setSelectedId] = useState(courierCases[0].id)
  const [filter, setFilter] = useState('همه')
  const [search, setSearch] = useState('')
  const [decision, setDecision] = useState('')
  const [toast, setToast] = useState('')
  const selected = courierCases.find(item => item.id === selectedId) || courierCases[0]
  const list = courierCases.filter(item => (filter === 'همه' || item.flag === filter) && `${item.id} ${item.name} ${item.detail}`.includes(search.trim()))
  const notify = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2800) }
  return <main className="admissions-demo" dir="rtl">
    <aside className="admissions-demo-sidebar">
      <div className="admissions-demo-brand"><span className="admissions-demo-mark"><i /><b>↔</b></span><span><strong>مونو</strong><small>لجستیک هوشمند</small></span></div>
      <nav aria-label="منوی پذیرش">{(['داشبورد', 'پیک‌ها', 'کسب‌وکارها', 'احراز و مدارک', 'فعال‌سازی', 'ردشده‌ها'] as const).map((item, index) => <button key={item} className={item === 'پیک‌ها' ? 'active' : ''} onClick={() => item === 'داشبورد' ? onNavigate('overview') : item === 'کسب‌وکارها' ? onSectionChange?.('کسب‌وکارها') : item === 'احراز و مدارک' ? onSectionChange?.('مدارک') : item === 'فعال‌سازی' ? onSectionChange?.('فعال‌سازی') : item === 'ردشده‌ها' ? onSectionChange?.('ردشده‌ها') : undefined}><span className="admissions-demo-navicon"><AdmissionNavIcon index={index} /></span>{item}</button>)}</nav>
      <div className="admissions-demo-profile"><span className="admissions-demo-avatar">م</span><span><strong>کارشناس پذیرش مونو</strong><small>تیم پذیرش و احراز</small></span></div><button className="admissions-demo-logout" onClick={() => notify('برای خروج از اتصال API استفاده کن.')}>خروج</button>
    </aside>
    <div className="admissions-demo-workspace courier-admission-workspace">
      <header className="admissions-demo-header"><div className="admissions-demo-header-actions"><button className="admissions-demo-secondary" onClick={() => notify('پرونده از صف بررسی خارج شد؛ تغییری در دادهٔ نمایشی ذخیره نشد.')}>خروج از بررسی</button><button className="admissions-demo-primary" onClick={() => notify('ثبت پیک واقعی به اتصال API نیاز دارد.')}>ثبت پیک جدید</button></div><div><h1>ثبت‌نام و پذیرش پیک‌ها</h1><p>بررسی هویت، مدارک، اطلاعات بانکی، وسیله، مناطق و الزامات اعتماد پیش از فعال‌سازی</p></div></header>
      <section className="admissions-demo-kpis courier-admission-kpis">{[['درخواست باز','۲۸'],['نیازمند اقدام','۷'],['مدارک ناقص','۹'],['آماده تأیید','۶'],['فعال‌شده امروز','۱۲']].map(([label,value],i)=><article key={label}><small>{label}</small><strong className={`metric-tone-${i}`}>{value}</strong></article>)}</section>
      <div className="courier-admission-layout">
        <aside className="admissions-demo-card courier-case-list"><header><h2>درخواست‌های پیک</h2><span>۲۸ باز</span></header><input aria-label="جست‌وجوی پیک" placeholder="جست‌وجوی نام، موبایل یا شناسه" value={search} onChange={e=>setSearch(e.target.value)} /><div className="courier-case-filters">{['همه','اقدام','بررسی','آماده'].map(item=><button key={item} className={filter===item?'active':''} onClick={()=>setFilter(item)}>{item}</button>)}</div><div className="courier-case-items">{list.map(item=><button key={item.id} className={`courier-case ${selectedId===item.id?'selected':''}`} onClick={()=>setSelectedId(item.id)}><span className={`priority priority-${item.tone}`}>{item.flag}</span><bdi>{item.id}</bdi><strong>{item.name} · {item.detail}</strong><small>{item.age}</small></button>)}{list.length===0&&<p className="admission-empty-row">پرونده‌ای پیدا نشد.</p>}</div></aside>
        <div className="courier-admission-detail">
          <section className="admissions-demo-card courier-selected"><header><span className="priority priority-بالا">در بررسی</span><div><h2>{selected.id} — {selected.name}</h2><p>موبایل: ۰۹۱۲ ۳۴۵ ۶۷۸۹ · نوع فعالیت: موتورسیکلت · منطقهٔ ترجیحی: مرکزی · ثبت‌نام: امروز ۰۹:۴۲</p></div></header><div className="courier-review-status">{[['اعتماد','تأیید'],['مدارک وسیله','در بررسی'],['بانک','تأیید'],['هویت','تأیید']].map(([a,b])=><span key={a}><small>{a}</small><b className={b==='تأیید'?'ok':'pending'}>{b}</b></span>)}</div></section>
          <div className="courier-info-grid"><section className="admissions-demo-card courier-info-card"><h2>هویت و اطلاعات پایه</h2>{[['نام و نام خانوادگی',selected.name],['کد ملی','۰۰۱۲۳۴۵۶۷۸'],['موبایل','۰۹۱۲ ۳۴۵ ۶۷۸۹'],['احراز هویت','تأییدشده'],['شبا','IR ۸۲۰۱...۴۵۶۷'],['تطبیق مالک حساب','تأیید']].map(([a,b])=><div key={a}><span>{a}</span><b className={b==='تأییدشده'||b==='تأیید'?'ok':''}>{b}</b></div>)}</section><section className="admissions-demo-card courier-info-card"><h2>شیوهٔ فعالیت و محدوده</h2>{[['شیوه حمل','موتورسیکلت'],['وسیله','VEH-REG-۰۷۸'],['پلاک','۱۲الف۳۴۵'],['منطقهٔ اصلی','مرکزی'],['مناطق انتخابی','مرکزی، شمال'],['ظرفیت پیشنهادی','۳ مأموریت']].map(([a,b])=><div key={a}><span>{a}</span><b>{b}</b></div>)}</section></div>
          <div className="courier-check-decision"><section className="admissions-demo-card courier-checklist"><h2>چک‌لیست پذیرش</h2>{courierChecks.map(([label,status,time,state])=><div key={label}><span>{label}</span><small>{time}</small><b className={state==='done'?'ok':'pending'}>{status}</b></div>)}</section><section className="admissions-demo-card courier-decision"><h2>تصمیم پذیرش</h2><div className="decision-summary"><b>نتیجهٔ بررسی</b><p>هویت، حساب بانکی و اعتماد تأیید شده‌اند. فقط بررسی بیمهٔ وسیله باقی مانده است.</p></div><ol>{[['۰۹:۴۲','ثبت‌نام تکمیل شد'],['۰۹:۴۸','هویت تأیید شد'],['۰۹:۵۱','شبا تطبیق شد'],['۱۰:۰۴','مدارک وسیله ارسال شد']].map(([time,event])=><li key={time}><small>{time}</small><span>{event}</span></li>)}</ol><button onClick={()=>setDecision('درخواست مدرک')}>درخواست مدرک</button><button className="danger" onClick={()=>setDecision('رد درخواست')}>رد درخواست</button><button className="admissions-demo-primary" onClick={()=>setDecision('تأیید و فعال‌سازی')}>تأیید و فعال‌سازی</button></section></div>
        </div>
      </div>
    </div>
    {toast&&<div role="status" className="admissions-demo-toast">{toast}</div>}
    {decision&&<div className="admissions-demo-backdrop" onClick={()=>setDecision('')}><section role="dialog" aria-modal="true" onClick={e=>e.stopPropagation()}><button className="admissions-demo-close" onClick={()=>setDecision('')} aria-label="بستن">×</button><h2>{decision}</h2><p>این پنل روی دادهٔ نمایشی کار می‌کند. برای اعمال تصمیم واقعی، اتصال API و مجوز پذیرش لازم است.</p><button className="admissions-demo-primary" onClick={()=>{setDecision('');notify('تصمیم در نسخهٔ نمایشی ثبت نشد.')}}>متوجه شدم</button></section></div>}
  </main>
}

const businessCases = [
  { id: 'BIZ-REG-۰۰۳۴', name: 'فروشگاه سپهر', detail: 'اطلاعات حقوقی', age: '۱۲ دقیقه پیش', flag: 'بررسی', tone: 'review' },
  { id: 'BIZ-REG-۰۰۳۱', name: 'سوپرمارکت آریا', detail: 'مدارک حقوقی', age: '۲۶ دقیقه پیش', flag: 'اقدام', tone: 'action' },
  { id: 'BIZ-REG-۰۰۲۹', name: 'مارکت ونک', detail: 'شعب و سرویس', age: '۳۸ دقیقه پیش', flag: 'بررسی', tone: 'review' },
  { id: 'BIZ-REG-۰۰۲۷', name: 'فروشگاه باران', detail: 'قرارداد', age: '۴۵ دقیقه پیش', flag: 'اقدام', tone: 'action' },
  { id: 'BIZ-REG-۰۰۲۳', name: 'هایپر پالادیوم', detail: 'روش اتصال', age: '۵۲ دقیقه پیش', flag: 'بررسی', tone: 'review' },
  { id: 'BIZ-REG-۰۰۲۱', name: 'فروشگاه اطلس', detail: 'تأیید نهایی', age: '۱ ساعت پیش', flag: 'آماده', tone: 'ready' },
]
const businessChecks = [['اسناد ثبتی شرکت', 'تأیید', 'امروز ۱۰:۲۲', 'done'], ['شناسه ملی و مالیاتی', 'تأیید', 'امروز ۱۰:۲۵', 'done'], ['مالکیت حساب بانکی', 'تأیید', 'امروز ۱۰:۲۸', 'done'], ['اطلاعات شعب', 'تأیید', '۳ شعبه', 'done'], ['قرارداد خدمات', 'در بررسی', 'پیش‌نویس ۱', 'pending'], ['تنظیم مالی', 'تأیید', 'ماهانه', 'done'], ['روش اتصال', 'تأیید', 'داشبورد', 'done']]

function BusinessAdmissionPage({ onNavigate, onSectionChange }: Props) {
  const [selectedId, setSelectedId] = useState(businessCases[0].id)
  const [filter, setFilter] = useState('همه')
  const [search, setSearch] = useState('')
  const [decision, setDecision] = useState('')
  const [toast, setToast] = useState('')
  const selected = businessCases.find(item => item.id === selectedId) || businessCases[0]
  const list = businessCases.filter(item => (filter === 'همه' || item.flag === filter) && `${item.id} ${item.name} ${item.detail}`.includes(search.trim()))
  const notify = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2800) }
  return <main className="admissions-demo" dir="rtl">
    <aside className="admissions-demo-sidebar">
      <div className="admissions-demo-brand"><span className="admissions-demo-mark"><i /><b>↔</b></span><span><strong>مونو</strong><small>لجستیک هوشمند</small></span></div>
      <nav aria-label="منوی پذیرش">{(['داشبورد', 'پیک‌ها', 'کسب‌وکارها', 'احراز و مدارک', 'فعال‌سازی', 'ردشده‌ها'] as const).map((item, index) => <button key={item} className={item === 'کسب‌وکارها' ? 'active' : ''} onClick={() => item === 'داشبورد' ? onNavigate('overview') : item === 'پیک‌ها' ? onSectionChange?.('پیک‌ها') : item === 'احراز و مدارک' ? onSectionChange?.('مدارک') : item === 'فعال‌سازی' ? onSectionChange?.('فعال‌سازی') : item === 'ردشده‌ها' ? onSectionChange?.('ردشده‌ها') : undefined}><span className="admissions-demo-navicon"><AdmissionNavIcon index={index} /></span>{item}</button>)}</nav>
      <div className="admissions-demo-profile"><span className="admissions-demo-avatar">م</span><span><strong>کارشناس پذیرش مونو</strong><small>تیم پذیرش و احراز</small></span></div><button className="admissions-demo-logout" onClick={() => notify('برای خروج از اتصال API استفاده کن.')}>خروج</button>
    </aside>
    <div className="admissions-demo-workspace business-admission-workspace">
      <header className="admissions-demo-header"><div className="admissions-demo-header-actions"><button className="admissions-demo-secondary" onClick={() => notify('پرونده از صف بررسی خارج شد؛ تغییری در دادهٔ نمایشی ذخیره نشد.')}>خروج از بررسی</button><button className="admissions-demo-primary" onClick={() => notify('ثبت کسب‌وکار واقعی به اتصال API نیاز دارد.')}>ثبت کسب‌وکار</button></div><div><h1>ثبت‌نام و پذیرش کسب‌وکارها</h1><p>بررسی هویت حقوقی، شعب، سرویس، قرارداد، مالی و روش اتصال پیش از فعال‌سازی</p></div></header>
      <section className="admissions-demo-kpis courier-admission-kpis">{[['درخواست باز','۶'],['نیازمند اقدام','۲'],['مدارک ناقص','۲'],['قرارداد آماده','۱'],['آماده فعال‌سازی','۱']].map(([label,value],i)=><article key={label}><small>{label}</small><strong className={`metric-tone-${i}`}>{value}</strong></article>)}</section>
      <div className="courier-admission-layout">
        <aside className="admissions-demo-card courier-case-list"><header><h2>درخواست‌های کسب‌وکار</h2><span>۶ باز</span></header><input aria-label="جست‌وجوی کسب‌وکار" placeholder="جست‌وجوی نام، شناسه یا مدیر" value={search} onChange={e=>setSearch(e.target.value)} /><div className="courier-case-filters">{['همه','اقدام','بررسی','آماده'].map(item=><button key={item} className={filter===item?'active':''} onClick={()=>setFilter(item)}>{item}</button>)}</div><div className="courier-case-items">{list.map(item=><button key={item.id} className={`courier-case ${selectedId===item.id?'selected':''}`} onClick={()=>setSelectedId(item.id)}><span className={`priority priority-${item.tone}`}>{item.flag}</span><bdi>{item.id}</bdi><strong>{item.name} · {item.detail}</strong><small>{item.age}</small></button>)}{list.length===0&&<p className="admission-empty-row">پرونده‌ای پیدا نشد.</p>}</div></aside>
        <div className="courier-admission-detail">
          <section className="admissions-demo-card courier-selected"><header><span className="priority priority-بالا">در بررسی</span><div><h2>{selected.id} — {selected.name}</h2><p>نوع شخصیت: حقوقی · مدیر مسئول: مهدی کاظمی · شهر: تهران · ثبت‌نام: امروز ۱۰:۱۲</p></div></header><div className="courier-review-status business-review-status">{[['اتصال','داشبورد'],['قرارداد','پیش‌نویس'],['شعب','۳ شعبه'],['هویت حقوقی','تأیید']].map(([a,b])=><span key={a}><small>{a}</small><b className={b==='تأیید'?'ok':'pending'}>{b}</b></span>)}</div></section>
          <div className="courier-info-grid"><section className="admissions-demo-card courier-info-card"><h2>هویت حقوقی و مالی</h2>{[['نام حقوقی','فروشگاه سپهر تجارت'],['شناسه ملی','۱۰۱۰۱۲۳۴۵۶۷'],['کد اقتصادی','۴۱۱۱۱۲۲۳۳۴۴'],['مدیر مسئول','مهدی کاظمی'],['موبایل سازمانی','۰۹۱۲ ۲۲۲ ۷۷۸۸'],['شبا تسویه','IR ۳۴۰۱...۸۹۱۲']].map(([a,b])=><div key={a}><span>{a}</span><b>{b}</b></div>)}</section><section className="admissions-demo-card courier-info-card"><h2>سرویس، شعب و اتصال</h2>{[['تعداد شعب','۳'],['شعب فعال اولیه','ونک، جردن، میرداماد'],['سرویس','ارسال شهری'],['حد شعاع','۵ کیلومتر'],['روش اتصال','داشبورد کسب‌وکار'],['بارگذاری فایل','فعال']].map(([a,b])=><div key={a}><span>{a}</span><b className={a==='روش اتصال'?'link-blue':''}>{b}</b></div>)}</section></div>
          <div className="courier-check-decision"><section className="admissions-demo-card courier-checklist business-checklist"><h2>چک‌لیست پذیرش</h2>{businessChecks.map(([label,status,time,state])=><div key={label}><span>{label}</span><small>{time}</small><b className={state==='done'?'ok':'pending'}>{status}</b></div>)}</section><section className="admissions-demo-card courier-decision"><h2>تصمیم پذیرش</h2><div className="decision-summary"><b>نتیجه بررسی</b><p>هویت حقوقی، بانک و شعب تأیید شده‌اند. فقط قرارداد خدمات نیازمند تأیید نهایی است.</p></div><h3>روش اتصال پس از فعال‌سازی</h3><div className="connection-modes"><span>داشبورد</span><span>API</span><span>فایل</span></div><small className="connection-note">آخرین اقدام: تأیید شعب · ۱۰:۴۰</small><button onClick={()=>setDecision('درخواست اطلاعات')}>درخواست اطلاعات</button><button className="danger" onClick={()=>setDecision('رد درخواست')}>رد درخواست</button><button className="admissions-demo-primary" onClick={()=>setDecision('تأیید و فعال‌سازی')}>تأیید و فعال‌سازی</button></section></div>
        </div>
      </div>
    </div>
    {toast&&<div role="status" className="admissions-demo-toast">{toast}</div>}
    {decision&&<div className="admissions-demo-backdrop" onClick={()=>setDecision('')}><section role="dialog" aria-modal="true" onClick={e=>e.stopPropagation()}><button className="admissions-demo-close" onClick={()=>setDecision('')} aria-label="بستن">×</button><h2>{decision}</h2><p>این پنل روی دادهٔ نمایشی کار می‌کند. برای اعمال تصمیم واقعی، اتصال API و مجوز پذیرش لازم است.</p><button className="admissions-demo-primary" onClick={()=>{setDecision('');notify('تصمیم در نسخهٔ نمایشی ثبت نشد.')}}>متوجه شدم</button></section></div>}
  </main>
}

const verificationCases = [
  { id: 'VER-۰۲۸۴', applicantId: 'CR-REG-۰۱۴۲', name: 'سعید مرادی', kind: 'پیک', document: 'کارت ملی', issue: 'عدم تطابق تصویر', priority: 'بحرانی', tone: 'action', validity: 'معتبر', face: '۶۲٪' },
  { id: 'VER-۰۲۸۱', applicantId: 'BIZ-REG-۰۰۳۱', name: 'سوپرمارکت آریا', kind: 'کسب‌وکار', document: 'روزنامه رسمی', issue: 'ناقص', priority: 'بالا', tone: 'action', validity: 'معتبر', face: '—' },
  { id: 'VER-۰۲۷۹', applicantId: 'CR-REG-۰۱۴۸', name: 'رضا احمدی', kind: 'پیک', document: 'بیمه وسیله', issue: 'در بررسی', priority: 'متوسط', tone: 'review', validity: 'تا ۱۴۰۵/۰۸/۲۱', face: '—' },
  { id: 'VER-۰۲۷۶', applicantId: 'BIZ-REG-۰۰۳۴', name: 'فروشگاه سپهر', kind: 'کسب‌وکار', document: 'حساب بانکی', issue: 'تأیید مالکیت', priority: 'عادی', tone: 'ready', validity: 'معتبر', face: '—' },
  { id: 'VER-۰۲۷۳', applicantId: 'CR-REG-۰۱۳۴', name: 'امیر رضایی', kind: 'پیک', document: 'سفته الکترونیک', issue: 'در بررسی', priority: 'متوسط', tone: 'review', validity: 'معتبر', face: '—' },
  { id: 'VER-۰۲۶۸', applicantId: 'CR-REG-۰۱۲۸', name: 'مهدی نادری', kind: 'پیک', document: 'شبا', issue: 'عدم تطابق', priority: 'بالا', tone: 'action', validity: 'معتبر', face: '—' },
  { id: 'VER-۰۲۶۲', applicantId: 'BIZ-REG-۰۰۲۷', name: 'فروشگاه باران', kind: 'کسب‌وکار', document: 'شناسه ملی', issue: 'تأیید', priority: 'عادی', tone: 'ready', validity: 'معتبر', face: '—' },
]

function VerificationDocumentsPage({ onNavigate, onSectionChange }: Props) {
  const [selectedId, setSelectedId] = useState(verificationCases[0].id)
  const [filter, setFilter] = useState('همه')
  const [search, setSearch] = useState('')
  const [toast, setToast] = useState('')
  const selected = verificationCases.find(item => item.id === selectedId) || verificationCases[0]
  const list = verificationCases.filter(item => (filter === 'همه' || item.kind === filter || item.document.includes(filter)) && `${item.id} ${item.applicantId} ${item.name} ${item.document} ${item.issue}`.includes(search.trim()))
  const notify = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2800) }
  return <main className="admissions-demo verification-demo" dir="rtl">
    <aside className="admissions-demo-sidebar">
      <div className="admissions-demo-brand"><span className="admissions-demo-mark"><i /><b>↔</b></span><span><strong>مونو</strong><small>لجستیک هوشمند</small></span></div>
      <nav aria-label="منوی پذیرش">{(['داشبورد', 'پیک‌ها', 'کسب‌وکارها', 'احراز و مدارک', 'فعال‌سازی', 'ردشده‌ها'] as const).map((item, index) => <button key={item} className={item === 'احراز و مدارک' ? 'active' : ''} onClick={() => item === 'داشبورد' ? onNavigate('overview') : item === 'پیک‌ها' ? onSectionChange?.('پیک‌ها') : item === 'کسب‌وکارها' ? onSectionChange?.('کسب‌وکارها') : item === 'فعال‌سازی' ? onSectionChange?.('فعال‌سازی') : item === 'ردشده‌ها' ? onSectionChange?.('ردشده‌ها') : undefined}><span className="admissions-demo-navicon"><AdmissionNavIcon index={index} /></span>{item}</button>)}</nav>
      <div className="admissions-demo-profile"><span className="admissions-demo-avatar">م</span><span><strong>کارشناس پذیرش مونو</strong><small>تیم پذیرش و احراز</small></span></div><button className="admissions-demo-logout" onClick={() => notify('برای خروج از اتصال API استفاده کن.')}>خروج</button>
    </aside>
    <div className="admissions-demo-workspace verification-workspace">
      <header className="admissions-demo-header"><div className="admissions-demo-header-actions"><button className="admissions-demo-secondary" onClick={() => notify('گزارش انقضا در این نسخهٔ نمایشی تولید نشد.')}>گزارش انقضا</button><button className="admissions-demo-secondary" onClick={() => notify('قواعد احراز در نسخهٔ نمایشی قابل تغییر نیست.')}>قواعد احراز</button></div><div><h1>احراز و مدارک</h1><p>صف متمرکز بررسی هویت، اسناد حقوقی، حساب بانکی، مدارک وسیله و انقضاها</p></div></header>
      <section className="admissions-demo-kpis verification-kpis">{[['در انتظار بررسی','۲۳'],['رد احراز','۳'],['مدرک ناقص','۸'],['انقضا تا ۳۰ روز','۵'],['آماده تأیید','۷']].map(([label,value],i)=><article key={label}><small>{label}</small><strong className={`metric-tone-${i}`}>{value}</strong></article>)}</section>
      <div className="verification-tabs" role="tablist" aria-label="فیلتر پرونده‌ها">{['همه','پیک‌ها','کسب‌وکارها','هویت','بانک','وسیله'].map(item=><button key={item} role="tab" aria-selected={filter===item} className={filter===item?'active':''} onClick={()=>setFilter(item)}>{item}</button>)}</div>
      <div className="verification-layout">
        <section className="admissions-demo-card verification-list"><header><h2>صف بررسی</h2><span>۲۳ باز</span></header><input aria-label="جست‌وجوی مدرک" placeholder="جست‌وجوی شناسه، نام یا نوع مدرک" value={search} onChange={e=>setSearch(e.target.value)} /><div className="verification-items">{list.map(item=><button key={item.id} className={`verification-case ${selected.id===item.id?'selected':''}`} onClick={()=>setSelectedId(item.id)}><span className={`priority priority-${item.tone}`}>{item.priority}</span><bdi>{item.id}</bdi><strong>{item.kind} • {item.applicantId} • {item.document}</strong><small>{item.name} · {item.issue}</small></button>)}{list.length===0&&<p className="admission-empty-row">مدرکی با این مشخصات پیدا نشد.</p>}</div><footer>{list.length} مورد از ۲۳ مورد نمونه</footer></section>
        <div className="verification-detail">
          <section className="admissions-demo-card verification-summary"><span className={`priority priority-${selected.tone}`}>{selected.priority}</span><div><h2>{selected.id} — احراز هویت {selected.name}</h2><p>{selected.kind} • {selected.applicantId} • موبایل ۰۹۱۲ ۶۷۸ ۴۵۲۱ • ارسال مدرک: امروز ۱۰:۳۱</p></div><div className="verification-meta"><span><small>مدرک</small><b>{selected.document}</b></span><span><small>اعتبار</small><b className="ok">{selected.validity}</b></span><span><small>تطبیق چهره</small><b className="pending">{selected.face}</b></span><span><small>اقدام</small><b>بررسی دستی</b></span></div></section>
          <section className="admissions-demo-card verification-preview"><header><h2>پیش‌نمایش مدرک</h2><span>{selected.document}</span></header><div className="document-mock"><div className="document-seal">مونو</div><div><b>نمونه مدرک هویتی</b><span>نام: {selected.name}</span><span>کد ملی: ۰۰۴۵۶۷۸۹۱۲</span><span>تاریخ تولد: ۱۳۷۴/۰۵/۱۷</span></div><p>تصویر برای بررسی اپراتور نمایش داده می‌شود؛ اصل فایل در مخزن فایل نگهداری می‌شود.</p></div></section>
          <div className="verification-lower"><section className="admissions-demo-card verification-evidence"><h2>شواهد و مغایرت</h2><div className="evidence-values"><span><b>۰۰۴۵۶۷۸۹۱۲</b><small>کد ملی فرم</small></span><i>↔</i><span><b>۰۰۴۵۶۷۸۹۱۲</b><small>کد ملی مدرک</small></span><span><b>{selected.name}</b><small>نام</small></span><span><b>{selected.face}</b><small>تطبیق چهره</small></span><span><b>۸۵٪</b><small>حد پذیرش</small></span></div><div className="evidence-alert"><b>نیازمند بررسی دستی</b><span>شناسه‌ها منطبق‌اند، اما تطبیق چهره پایین‌تر از حد پذیرش است.</span></div><h3>چک‌لیست بررسی</h3>{[['خوانایی مدرک','کیفیت مناسب','تأیید'],['اعتبار مدرک','منقضی نشده','تأیید'],['تطبیق کد ملی','مطابق فرم','تأیید'],['تطبیق نام','مطابق فرم','تأیید'],['تطبیق چهره',selected.face,'رد']].map(([a,b,c])=><div className="verification-check" key={a}><span>{a}</span><small>{b}</small><b className={c==='رد'?'error':'ok'}>{c}</b></div>)}</section>
            <section className="admissions-demo-card verification-decision"><h2>تصمیم بررسی</h2><div className="decision-summary"><b>پیشنهاد سیستم</b><p>به‌دلیل تطبیق چهره ۶۲٪، مدرک را خودکار تأیید نکنید.</p></div><div className="verification-audit"><span>۱۰:۳۶ • سیستم</span><span>آخرین تغییر</span><b>کارشناس احراز</b><small>بررسی‌کننده</small></div><button onClick={()=>notify('درخواست تصویر جدید در نسخهٔ نمایشی ثبت نشد.')}>درخواست تصویر جدید</button><button className="danger" onClick={()=>notify('رد مدرک در نسخهٔ نمایشی ثبت نشد.')}>رد مدرک</button><button className="admissions-demo-primary" onClick={()=>notify('تأیید دستی در نسخهٔ نمایشی ثبت نشد.')}>تأیید دستی</button></section></div>
        </div>
      </div>
    </div>
    {toast&&<div role="status" className="admissions-demo-toast">{toast}</div>}
  </main>
}

const activationCases = [
  { id: 'ACT-۰۱۴۲', applicantId: 'CR-REG-۰۱۲۲', type: 'پیک', name: 'علی موسوی', status: 'آماده', issue: 'بدون مانع', mode: 'موتورسیکلت', area: 'مرکزی' },
  { id: 'ACT-۰۱۳۹', applicantId: 'BIZ-REG-۰۰۲۱', type: 'کسب‌وکار', name: 'فروشگاه اطلس', status: 'آماده', issue: 'بدون مانع', mode: 'ارسال شهری', area: 'مرکزی' },
  { id: 'ACT-۰۱۳۷', applicantId: 'CR-REG-۰۱۴۸', type: 'پیک', name: 'رضا احمدی', status: 'مانع', issue: 'بیمه', mode: 'موتورسیکلت', area: 'شمال' },
  { id: 'ACT-۰۱۳۳', applicantId: 'CR-REG-۰۱۳۹', type: 'پیک', name: 'مجتبی کریمی', status: 'آماده', issue: 'بدون مانع', mode: 'موتورسیکلت', area: 'مرکزی' },
  { id: 'ACT-۰۱۲۹', applicantId: 'BIZ-REG-۰۰۳۴', type: 'کسب‌وکار', name: 'فروشگاه سپهر', status: 'مانع', issue: 'قرارداد', mode: 'ارسال شهری', area: 'تهران' },
  { id: 'ACT-۰۱۲۴', applicantId: 'CR-REG-۰۱۳۴', type: 'پیک', name: 'امیر رضایی', status: 'آماده', issue: 'بدون مانع', mode: 'موتورسیکلت', area: 'مرکزی، شمال' },
]

function ActivationPage({ onNavigate, onSectionChange }: Props) {
  const [selectedId, setSelectedId] = useState(activationCases[0].id)
  const [filter, setFilter] = useState('همه')
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState('')
  const [toast, setToast] = useState('')
  const selected = activationCases.find(item => item.id === selectedId) || activationCases[0]
  const list = activationCases.filter(item => (filter === 'همه' || filter === 'دارای مانع' && item.status === 'مانع' || filter === item.type) && `${item.id} ${item.applicantId} ${item.name}`.includes(search.trim()))
  const notify = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2800) }
  const activationChecks = [['احراز هویت','تکمیل','بدون مانع','ok'],['مدارک لازم','تکمیل','همه معتبر','ok'],['حساب بانکی','تکمیل','مالکیت تأیید','ok'],['اعتماد و تضمین','تکمیل','ثبت شده','ok'],['مناطق و وسیله','تکمیل','تنظیم شده','ok']]
  return <main className="admissions-demo activation-demo" dir="rtl">
    <aside className="admissions-demo-sidebar"><div className="admissions-demo-brand"><span className="admissions-demo-mark"><i /><b>↔</b></span><span><strong>مونو</strong><small>لجستیک هوشمند</small></span></div>
      <nav aria-label="منوی پذیرش">{(['داشبورد','پیک‌ها','کسب‌وکارها','احراز و مدارک','فعال‌سازی','ردشده‌ها'] as const).map((item,index)=><button key={item} className={item==='فعال‌سازی'?'active':''} onClick={()=>item==='داشبورد'?onNavigate('overview'):item==='پیک‌ها'?onSectionChange?.('پیک‌ها'):item==='کسب‌وکارها'?onSectionChange?.('کسب‌وکارها'):item==='احراز و مدارک'?onSectionChange?.('مدارک'):item==='ردشده‌ها'?onSectionChange?.('ردشده‌ها'):undefined}><span className="admissions-demo-navicon"><AdmissionNavIcon index={index}/></span>{item}</button>)}</nav>
      <div className="admissions-demo-profile"><span className="admissions-demo-avatar">م</span><span><strong>کارشناس پذیرش مونو</strong><small>تیم پذیرش و احراز</small></span></div><button className="admissions-demo-logout" onClick={()=>notify('برای خروج از اتصال API استفاده کن.')}>خروج</button>
    </aside>
    <div className="admissions-demo-workspace activation-workspace">
      <header className="admissions-demo-header"><div className="admissions-demo-header-actions"><button className="admissions-demo-secondary" onClick={()=>notify('گزارش فعال‌سازی در این نسخهٔ نمایشی تولید نشد.')}>گزارش فعال‌سازی</button><button className="admissions-demo-secondary" onClick={()=>notify('قواعد فعال‌سازی در نسخهٔ نمایشی قابل تغییر نیست.')}>تنظیم قواعد</button></div><div><h1>فعال‌سازی</h1><p>گیت نهایی پیش از ورود پیک یا کسب‌وکار به محیط عملیاتی مونو</p></div></header>
      <section className="admissions-demo-kpis activation-kpis">{[['آماده فعال‌سازی','۷'],['دارای مانع','۳'],['پیک آماده','۵'],['کسب‌وکار آماده','۲'],['فعال‌شده امروز','۹']].map(([label,value],i)=><article key={label}><small>{label}</small><strong className={`metric-tone-${i}`}>{value}</strong></article>)}</section>
      <div className="verification-tabs" role="tablist" aria-label="فیلتر فعال‌سازی">{['همه','پیک','کسب‌وکار','دارای مانع'].map(item=><button key={item} role="tab" aria-selected={filter===item} className={filter===item?'active':''} onClick={()=>setFilter(item)}>{item==='پیک'?'پیک‌ها':item==='کسب‌وکار'?'کسب‌وکارها':item}</button>)}</div>
      <div className="activation-layout">
        <section className="admissions-demo-card activation-list"><header><h2>صف فعال‌سازی</h2><span>۷ آماده</span></header><input aria-label="جست‌وجوی نام یا شناسه" placeholder="جست‌وجوی نام یا شناسه" value={search} onChange={e=>setSearch(e.target.value)}/><div className="verification-items">{list.map(item=><button key={item.id} className={`verification-case ${selected.id===item.id?'selected':''}`} onClick={()=>setSelectedId(item.id)}><span className={`priority ${item.status==='مانع'?'priority-action':'priority-ready'}`}>{item.status}</span><bdi>{item.id}</bdi><strong>{item.type} • {item.applicantId}</strong><small>{item.name} • {item.status==='مانع'?`مانع: ${item.issue}`:'آماده'}</small></button>)}{list.length===0&&<p className="admission-empty-row">موردی برای این فیلتر پیدا نشد.</p>}</div><footer>{list.length} مورد از ۷ مورد نمونه</footer></section>
        <div className="activation-detail">
          <section className="admissions-demo-card activation-summary"><span className={`priority ${selected.status==='مانع'?'priority-action':'priority-ready'}`}>{selected.status}</span><div><h2>{selected.id} — {selected.name}</h2><p>{selected.type} • {selected.applicantId} • {selected.mode} • منطقه {selected.area} • بررسی نهایی: امروز ۱۱:۰۶</p></div><div className="activation-gates">{[['احراز','تکمیل'],['مدارک','تکمیل'],['دسترسی',selected.status==='مانع'?'مسدود':'آماده'],['مانع',selected.issue]].map(([a,b])=><span key={a}><small>{a}</small><b className={b==='تکمیل'||b==='آماده'||b==='بدون مانع'?'ok':'pending'}>{b}</b></span>)}</div></section>
          <div className="activation-columns"><section className="admissions-demo-card activation-card"><h2>حساب و دسترسی</h2>{[['حساب پیک','ایجاد شده'],['شماره موبایل','۰۹۱۲ ۵۵۵ ۱۸۴۲'],['نقش','پیک'],['ورود اولیه','رمز یک‌بارمصرف موبایل'],['کیف پول','ایجاد شده'],['شناسه عملیاتی','CR-۲۸۴']].map(([a,b])=><div key={a}><span>{a}</span><b className={b==='ایجاد شده'?'ok':''}>{b}</b></div>)}</section><section className="admissions-demo-card activation-card"><h2>تنظیمات عملیاتی</h2>{[['شیوه حمل',selected.mode],['مناطق مجاز',selected.area+', شمال'],['حد مأموریت همزمان','۳'],['شروع فعالیت','امروز'],['آموزش اولیه','تکمیل'],['وضعیت اولیه',selected.status==='مانع'?'غیرفعال تا تأیید':'آماده فعال‌سازی']].map(([a,b])=><div key={a}><span>{a}</span><b>{b}</b></div>)}</section></div>
          <div className="activation-lower"><section className="admissions-demo-card activation-checks"><h2>چک‌لیست آمادگی</h2>{activationChecks.map(([a,b,c,d])=><div key={a}><span>{a}</span><small>{c}</small><b className={selected.status==='مانع'&&a==='مدارک لازم'?'pending':d}>{selected.status==='مانع'&&a==='مدارک لازم'?'مانع':b}</b></div>)}<div><span>حساب عملیاتی</span><small>CR-۲۸۴</small><b className="ok">آماده</b></div></section><section className="admissions-demo-card activation-decision"><h2>تصمیم فعال‌سازی</h2><div className={`decision-summary ${selected.status==='مانع'?'activation-blocked':''}`}><b>{selected.status==='مانع'?'دارای مانع':'آماده فعال‌سازی'}</b><p>{selected.status==='مانع'?`مانع «${selected.issue}» باید رفع شود؛ فعال‌سازی ممکن نیست.`:'تمام کنترل‌های الزامی تکمیل شده‌اند و مانع باز وجود ندارد.'}</p></div><ol><li><small>۱۰:۴۲</small><span>تأیید احراز</span></li><li><small>۱۰:۵۸</small><span>آماده‌سازی حساب</span></li><li><small>۱۱:۰۶</small><span>بررسی نهایی</span></li></ol><button onClick={()=>setDialog('تعلیق فعال‌سازی')}>تعلیق فعال‌سازی</button><button className="admissions-demo-primary" disabled={selected.status==='مانع'} onClick={()=>setDialog('فعال‌سازی نهایی')}>فعال‌سازی نهایی</button></section></div>
        </div>
      </div>
    </div>
    {toast&&<div role="status" className="admissions-demo-toast">{toast}</div>}
    {dialog&&<div className="admissions-demo-backdrop" onClick={()=>setDialog('')}><section role="dialog" aria-modal="true" aria-labelledby="activation-dialog-title" onClick={e=>e.stopPropagation()}><button className="admissions-demo-close" onClick={()=>setDialog('')} aria-label="بستن">×</button><h2 id="activation-dialog-title">{dialog}</h2><p>این نما روی دادهٔ نمایشی کار می‌کند؛ وضعیت واقعی ایجاد یا تغییر نمی‌کند. اجرای عملیاتی به اتصال API و مجوز فعال‌سازی نیاز دارد.</p><button className="admissions-demo-primary" onClick={()=>{setDialog('');notify('تغییری در نسخهٔ نمایشی ثبت نشد.')}}>متوجه شدم</button></section></div>}
  </main>
}
