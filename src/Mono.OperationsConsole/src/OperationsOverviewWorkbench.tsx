import type { DashboardSummaryMetrics } from './DashboardSummaryCards'
import './operations-overview-workbench.css'

const fa = new Intl.NumberFormat('fa-IR')

const sampleMissions = [
  ['MIS-۱۰۴۸۲', 'مرکزی', 'در مسیر', 'CR-۲۰۴', '۱۲ دقیقه', 'عادی', 'حرکت به مقصد'],
  ['MIS-۱۰۴۷۹', 'شمال', 'در محل دریافت', 'CR-۱۱۸', '۲۱ دقیقه', 'در خطر', 'توقف ۷ دقیقه'],
  ['MIS-۱۰۴۷۴', 'غرب', 'جست‌وجوی پیک', '—', '۳۴ دقیقه', 'در خطر', 'بدون تخصیص'],
  ['MIS-۱۰۴۶۱', 'مرکزی', 'پیک جایگزین', 'CR-۳۱۱', '۲۹ دقیقه', 'بحرانی', 'جایگزین پیک قبلی شد'],
  ['MIS-۱۰۴۵۵', 'جنوب', 'تحویل‌گرفته‌شده', 'CR-۳۰۲', '۸ دقیقه', 'عادی', 'دریافت کامل شد'],
]

const sampleAlerts = [
  ['MIS-۱۰۴۶۱', 'پایش تخصیص مجدد', '۷ دقیقه پیش', 'بحرانی'],
  ['MIS-۱۰۴۷۹', 'توقف دریافت بیش از ۳ دقیقه', '۳ دقیقه پیش', 'در خطر'],
  ['MIS-۱۰۴۷۴', 'هنوز پیکی تخصیص نشده', '۱۰ دقیقه پیش', 'در خطر'],
  ['MIS-۱۰۴۳۰', 'تخصیص مجدد نیازمند پیگیری', '۱۲ دقیقه پیش', 'عادی'],
]

const sampleSummary: DashboardSummaryMetrics = {
  as_of: '2026-11-01T11:02:00Z', business_date: '2026-11-01', sla_horizon_minutes: 15,
  active_mission_count: 126, unassigned_mission_count: 14, online_courier_count: 83,
  sla_at_risk_count: 9, sla_overdue_count: 3, delivered_today_count: 248,
  on_time_delivered_today_count: 235, on_time_delivery_rate_percent: 94.6,
}

type Props = {
  demoMode: boolean
  summary: DashboardSummaryMetrics | null
  onOpenMissions: () => void
  onOpenDispatch: () => void
  onOpenSla: () => void
  onOpenPresence: () => void
  onRefresh: () => void
}

export default function OperationsOverviewWorkbench({ demoMode, summary, onOpenMissions, onOpenDispatch, onOpenSla, onOpenPresence, onRefresh }: Props) {
  const values = demoMode ? sampleSummary : summary
  const metric = (value: number | undefined) => value == null ? '—' : fa.format(value)
  return <div className="operations-overview-workbench" data-demo={demoMode}>
    <header className="overview-v2-heading">
      <div><h1>نمای کلی عملیات</h1><p>وضعیت لحظه‌ای شبکهٔ مونو، مأموریت‌ها، پیک‌ها، استاندارد خدمت و ظرفیت عملیاتی</p></div>
      <div className="overview-v2-actions"><button className="secondary-button" onClick={onRefresh} disabled={!demoMode && !summary}>نوسازی</button><button className="primary-button" disabled title="مأموریت پس از تأیید ارسال ایجاد می‌شود.">ایجاد مأموریت</button></div>
    </header>
    {demoMode && <div className="overview-demo-banner">داده‌های این صفحه نمایشی‌اند و فقط برای مقایسه با فیگما استفاده می‌شوند.</div>}
    <section className="overview-v2-kpis" aria-label="شاخص‌های عملیات">
      <button onClick={onOpenMissions}><small>تحویل به‌موقع امروز</small><strong className="good">{values?.on_time_delivery_rate_percent == null ? '—' : `${fa.format(values.on_time_delivery_rate_percent)}٪`}</strong><span>{demoMode ? 'امروز' : 'نرخ ثبت‌شده در API'}</span></button>
      <button onClick={onOpenDispatch}><small>مسیر فعال</small><strong>{demoMode ? '۲۸' : '—'}</strong><span>{demoMode ? 'میانگین ۲٫۶ مأموریت' : 'در API موجود نیست'}</span></button>
      <button onClick={onOpenPresence}><small>پیک آنلاین</small><strong>{metric(values?.online_courier_count)}</strong><span>{demoMode ? 'ظرفیت مؤثر V۱' : 'وضعیت حضور'}</span></button>
      <button onClick={onOpenSla}><small>در معرض نقض استاندارد</small><strong className="warning">{metric(values?.sla_at_risk_count)}</strong><span>{demoMode ? '۳ مورد بحرانی' : 'افق ارزیابی API'}</span></button>
      <button onClick={onOpenMissions}><small>مأموریت فعال</small><strong>{metric(values?.active_mission_count)}</strong><span>{demoMode ? '۱۸+ در یک ساعت اخیر' : 'مبنای API'}</span></button>
    </section>
    <div className="overview-v2-grid">
      <aside className="overview-v2-side">
        <section className="overview-v2-card overview-alerts"><header><h2>نیازمند اقدام</h2><span className="overview-count">{demoMode ? '۵ مورد' : '—'}</span></header>
          {demoMode ? <div className="overview-alert-list">{sampleAlerts.map(([id, title, time, state]) => <button key={id} onClick={onOpenSla}><span className={`overview-state ${state === 'بحرانی' ? 'critical' : state === 'در خطر' ? 'warning' : ''}`}>{state}</span><strong dir="ltr">{id}</strong><small>{title}</small><time>{time}</time></button>)}</div> : <div className="overview-empty">پس از اتصال به API، موارد نیازمند اقدام اینجا نمایش داده می‌شوند.</div>}
        </section>
        <section className="overview-v2-card overview-zone-health"><header><h2>سلامت ظرفیت مناطق</h2></header>
          {(demoMode ? [['مرکزی','۸۲٪','مناسب',82],['شمال','۶۴٪','پایش',64],['غرب','۴۱٪','کم',41]] : [['مرکزی','—','دادهٔ API',0],['شمال','—','دادهٔ API',0],['غرب','—','دادهٔ API',0]]).map(([zone,value,state,percent]) => <div className="overview-zone-row" key={zone}><span>{state}</span><b>{value}</b><strong>{zone}</strong><i><em style={{width:`${percent}%`}} /></i></div>)}
          {!demoMode && <small className="overview-card-note">برای مشاهدهٔ ظرفیت، زون و بازهٔ زمانی را انتخاب کنید.</small>}
        </section>
        <section className="overview-v2-card overview-service-health"><header><h2>سلامت سرویس‌ها</h2></header>
          {(demoMode ? [['API','۹۹٫۹٪','good'],['وب‌هوک','۹۹٫۹٪','good'],['درگاه نقشه','سالم','good'],['صف رویداد','۱ مورد در صف شکست','warning']] : [['API','—',''],['وب‌هوک','—',''],['درگاه نقشه','—',''],['صف رویداد','—','']]).map(([label,value,state]) => <div className="overview-service-row" key={label}><strong>{label}</strong><span className={state}>{value}</span></div>)}
        </section>
      </aside>
      <main className="overview-v2-main">
        <section className="overview-v2-card overview-live-missions"><header><div><span className="overview-live-dot">زنده</span><h2>نبض مأموریت‌ها</h2></div><button onClick={onOpenMissions}>مشاهدهٔ همهٔ مأموریت‌ها ←</button></header>
          <div className="overview-mission-table-wrap"><table><thead><tr><th>آخرین رویداد</th><th>استاندارد خدمت</th><th>ETA</th><th>پیک</th><th>وضعیت</th><th>شعبه</th><th>شناسه</th></tr></thead><tbody>{demoMode ? sampleMissions.map(([id,zone,state,courier,eta,sla,event])=><tr key={id}><td>{event}</td><td><span className={`overview-sla ${sla==='بحرانی'?'critical':sla==='در خطر'?'warning':''}`}>{sla}</span></td><td>{eta}</td><td>{courier}</td><td>{state}</td><td>{zone}</td><td dir="ltr">{id}</td></tr>) : <tr><td colSpan={7} className="overview-mission-empty">برای دریافت اطلاعات زنده به API عملیات متصل شوید.</td></tr>}</tbody></table></div>
        </section>
        <div className="overview-v2-bottom">
          <section className="overview-v2-card overview-network"><header><h2>شبکهٔ پیک و مسیر</h2></header>
            {[['پیک آنلاین',metric(values?.online_courier_count),''],['روی مسیر فعال',demoMode?'۴۶':'—','cyan'],['در محل دریافت',demoMode?'۱۲':'—','cyan'],['آمادهٔ دریافت مأموریت',demoMode?'۲۵':'—','good'],['مسیر فعال',demoMode?'۲۸':'—',''],['مسیر در معرض خطر',demoMode?'۴':'—','warning']].map(([label,value,tone])=><div className="overview-network-row" key={label}><strong className={tone}>{value}</strong><span>{label}</span></div>)}
            <p className="overview-info-note">ظرفیت مؤثر پیک، محدودیت‌ها و قواعد تخصیص را از API دریافت می‌کند.</p>
          </section>
          <section className="overview-v2-card overview-today"><header><h2>خلاصهٔ امروز</h2></header>
            {[['مشتری فعال','حنا'],['شعب فعال',demoMode?'۸':'—'],['سفارش امروز',metric(values?.delivered_today_count)],['کرایهٔ امروز',demoMode?'۷٫۴۸ م تومان':'—'],['خالص پیک‌ها',demoMode?'۶٫۶۶ م تومان':'—'],['رخداد باز',demoMode?'۵':'—']].map(([label,value],index)=><div className="overview-today-row" key={label}><span>{label}</span><strong className={index===4?'good':index===5?'warning':''}>{value}</strong></div>)}
            <p className="overview-info-note">ارجاع‌های لجستیکی حنا با شناسهٔ سفارش به مونو متصل می‌شوند.</p>
          </section>
        </div>
      </main>
    </div>
  </div>
}
