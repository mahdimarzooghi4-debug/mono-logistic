import { useMemo, useState } from 'react'
import './customers-demo-workbench.css'

const branches = [
  ['جردن', '۲۳'], ['میرداماد', '۱۸'], ['ونک', '۱۶'], ['یوسف‌آباد', '۱۴'], ['پاسداران', '۱۲'],
]
const orders = [
  ['HNA-839251', 'MIS-10479', 'جردن', 'علی رضایی', 'در محل دریافت', '۲۱ دقیقه', 'در خطر'],
  ['HNA-839246', 'MIS-10482', 'میرداماد', 'مریم عباسی', 'در مسیر', '۱۲ دقیقه', 'عادی'],
  ['HNA-839239', 'MIS-10474', 'ونک', 'محمد نادری', 'جست‌وجوی پیک', '۳۴ دقیقه', 'در خطر'],
  ['HNA-839230', 'MIS-10461', 'جردن', 'سارا کریمی', 'تخصیص مجدد', '۲۹ دقیقه', 'بحرانی'],
  ['HNA-839225', 'MIS-10455', 'پاسداران', 'رضا مرادی', 'تحویل‌گرفته‌شده', '۸ دقیقه', 'عادی'],
]

export default function CustomersDemoWorkbench() {
  const [query, setQuery] = useState('')
  const visibleOrders = useMemo(() => orders.filter(row => row.join(' ').toLocaleLowerCase().includes(query.toLocaleLowerCase())), [query])
  return <main className="customers-demo" dir="rtl">
    <header className="customers-demo-header"><div className="customers-demo-actions"><button className="secondary-button" disabled title="تنظیم واقعی سرویس از طریق API انجام می‌شود.">تنظیم سرویس</button><button className="primary-button" disabled title="ثبت واقعی مشتری از پنل مدیریت مشتریان انجام می‌شود.">ثبت مشتری</button></div><div><h1>مشتریان سازمانی</h1><p>حساب مشتری، شعب، سفارش‌ها، مأموریت‌ها و وضعیت یکپارچه‌سازی</p></div></header>
    <div className="customers-demo-banner">حنا مشتری فاز اول محصول است؛ مقادیر این صفحه نمونه‌اند و دادهٔ زنده نیستند.</div>
    <section className="customers-demo-kpis" aria-label="خلاصهٔ مشتریان">{[['مشتری فعال','۱'],['شعب فعال حنا','۸'],['سفارش فعال','۱۲۶'],['مأموریت‌های فعال','۱۲۶'],['سلامت API','۹۹٫۹۸٪']].map(([label,value])=><article key={label}><small>{label}</small><strong>{value}</strong></article>)}</section>
    <div className="customers-demo-main">
      <section className="customers-demo-panel customers-demo-list"><header><h2>مشتریان</h2><span className="customer-status">فعال</span></header><button className="customer-account active"><strong>حنا</strong><small>مشتری سازمانی API</small><small>۸ شعبه فعال • تحویل شهری • تهران</small><code dir="ltr">CUS-HANA-001</code></button><aside className="customer-referral"><strong>ارجاع لجستیکی از حنا</strong><p>اپراتور حنا در صورت نیاز، سفارش و مأموریت مرتبط را از پنل خودش به مونو ارجاع می‌دهد؛ مونو فقط زمینهٔ لجستیکی لازم برای رسیدگی را دریافت می‌کند.</p><small>گیرنده / سفارش‌دهنده · شماره‌های قابل تطبیق</small></aside></section>
      <section className="customers-demo-panel customer-account-detail"><header><div><h2>حنا</h2><p>تحویل شهری • اتصال API • محدوده سرویس ۵ کیلومتر • پیاده / موتور / خودرو</p></div><span className="customer-status">فعال</span></header>
        <div className="customer-summary">{[['نوع قرارداد','سازمانی'],['شعب فعال','۸'],['سفارش امروز','۲۴۸'],['استاندارد امروز','۹۴٫۶٪']].map(([k,v])=><div key={k}><small>{k}</small><strong>{v}</strong></div>)}</div>
        <section className="customer-subpanel"><header><h3>شعب حنا</h3><span>فعال</span></header><div className="customer-branch-grid">{branches.map(([name,count])=><article key={name}><strong>{name}</strong><span>فعال</span><small>{count} مأموریت</small></article>)}</div></section>
        <div className="customer-lower-grid"><section className="customer-subpanel"><header><h3>پیکربندی سرویس</h3></header><dl>{[['سرویس','تحویل شهری'],['حداکثر فاصله','۵ کیلومتر'],['Modeها','پیاده / موتور / خودرو'],['حداکثر Batch','۵ مأموریت'],['توقف دریافت','۳ دقیقه'],['تحویل','تأیید نام گیرنده و تحویل']].map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl></section><section className="customer-subpanel"><header><h3>API و وب‌هوک</h3><span className="customer-status">سالم</span></header><div className="customer-api-health"><strong>۹۹٫۹٪</strong><small>سلامت API</small></div><ul><li>آخرین درخواست · ۲۱ ثانیه پیش</li><li>آخرین وب‌هوک · ۱۸ ثانیه پیش</li><li>کنترل تکرار درخواست · فعال</li><li>امضای وب‌هوک HMAC · فعال</li></ul></section></div>
      </section>
    </div>
    <section className="customers-demo-panel customer-orders"><header><div><h2>سفارش‌ها و مأموریت‌های مرتبط</h2><small>۱۲۶ فعال</small></div><label>فیلتر <input aria-label="جست‌وجوی سفارش، مأموریت یا موبایل" placeholder="جست‌وجوی شناسه سفارش، مأموریت یا موبایل" value={query} onChange={e=>setQuery(e.target.value)} /></label></header><div className="customer-orders-table"><table><thead><tr>{['استاندارد خدمت','ETA','وضعیت','گیرنده','شعبه','مأموریت','شناسه سفارش'].map(x=><th key={x}>{x}</th>)}</tr></thead><tbody>{visibleOrders.map(([order,mission,branch,recipient,status,eta,sla])=><tr key={order}><td><span className={`customer-sla ${sla==='بحرانی'?'critical':sla==='در خطر'?'warning':''}`}>{sla}</span></td><td>{eta}</td><td>{status}</td><td>{recipient}</td><td>{branch}</td><td dir="ltr">{mission}</td><td dir="ltr">{order}</td></tr>)}{!visibleOrders.length&&<tr><td colSpan={7} className="customer-no-results">سفارشی با این جست‌وجو پیدا نشد.</td></tr>}</tbody></table></div>
    </section>
    <p className="customers-demo-note">وضعیت و آمار این پیش‌نمایش برای تطبیق با طرح فیگماست؛ دکمه‌های عملیاتی واقعی به API متصل نیستند.</p>
  </main>
}
