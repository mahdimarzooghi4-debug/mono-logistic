import { useState } from 'react'
import './incoming-call-demo-workbench.css'

export type IncomingCallScenario = 'matched' | 'unmatched' | 'multiple'

const matches = [
  { id: 'MIS-10479', order: 'HNA-839251', branch: 'شعبه مرکزی', status: 'در محل دریافت', committed: '۱۴:۳۰', eta: '۱۴:۵۱', courier: 'CR-118', route: 'مسیر فعال', destination: 'ونک، ملاصدرا', event: 'توقف دریافت بیش از ۳ دقیقه', risk: 'در معرض خطر' },
  { id: 'MIS-10482', order: 'HNA-839254', branch: 'شعبه شمال', status: 'در مسیر', committed: '۱۴:۴۵', eta: '۱۴:۵۰', courier: 'CR-204', route: 'مسیر مرکزی ۰۷', destination: 'میدان ونک', event: 'حرکت به مقصد', risk: 'عادی' },
  { id: 'MIS-10474', order: 'HNA-839247', branch: 'شعبه غرب', status: 'جست‌وجوی پیک', committed: '۱۴:۲۰', eta: '۳۴ دقیقه', courier: 'بدون پیک', route: 'بدون مسیر فعال', destination: 'میدان ونک', event: 'در انتظار پذیرش پیک', risk: 'در معرض خطر' },
]

export default function IncomingCallDemoWorkbench({ scenario, onClose, onOpenMission }: { scenario: IncomingCallScenario; onClose: () => void; onOpenMission: (id: string) => void }) {
  const [selected, setSelected] = useState(0)
  const [query, setQuery] = useState('')
  const [notice, setNotice] = useState('')
  const matched = scenario !== 'unmatched'
  const mission = matches[scenario === 'multiple' ? selected : 0]
  const visibleMatches = matches.filter((item) => `${item.id} ${item.order} ${item.branch} ${item.courier} علی رضایی حنا`.toLowerCase().includes(query.toLowerCase()))

  return <div className="incoming-call-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
    <aside className="incoming-call-panel" dir="rtl" role="dialog" aria-modal="true" aria-labelledby="incoming-call-title">
      <header className="incoming-call-header">
        <button type="button" className="incoming-call-close" aria-label="بستن" onClick={onClose}>×</button>
        <h2 id="incoming-call-title">تماس ورودی — {scenario === 'unmatched' ? 'بدون تطبیق' : 'زمینهٔ مأموریت'}</h2>
        <div className="incoming-call-state"><span className={matched ? 'caller-known' : 'caller-unknown'}>{matched ? 'شناخته‌شده' : 'ناشناخته'}</span><b>در حال زنگ</b></div>
        <strong className="incoming-call-number" dir="ltr">۰۹۱۲ ۳۴۵ ۶۷۸۹</strong>
        <p>مونو با دریافت شماره از اتصال تلفنی، زمینهٔ سفارش و مأموریت را خودکار برای اپراتور باز می‌کند.</p>
        <div className="incoming-call-demo-label">دادهٔ نمونه • اتصال تلفنی لازم است</div>
      </header>
      <div className="incoming-call-scroll">
        <section className="caller-identity">
          <span className="incoming-section-label">تماس‌گیرنده</span>
          <h3>{matched ? 'علی رضایی' : 'تماس‌گیرنده ناشناس'}</h3>
          <InfoRow label={matched ? 'نقش در سفارش' : 'وضعیت تطبیق'} value={matched ? 'گیرنده' : 'بدون تطبیق'} />
          <InfoRow label="مشتری سازمانی" value={matched ? 'حنا' : 'یافت نشد'} />
          <InfoRow label="تعداد سفارش فعال" value={scenario === 'unmatched' ? '۰ سفارش' : scenario === 'multiple' ? '۳ سفارش فعال' : '۱ سفارش'} />
        </section>

        {scenario === 'unmatched' ? <section className="unmatched-call-section">
          <div className="incoming-section-heading"><h3>نتیجهٔ تطبیق</h3><span className="incoming-chip muted">بدون تطبیق</span></div>
          <div className="incoming-empty-card"><strong>نتیجهٔ جست‌وجو</strong><p>شماره با هیچ سفارش یا مأموریت فعالی تطبیق پیدا نکرد؛ شماره برای جست‌وجوی دستی حفظ شده است.</p></div>
          <label className="incoming-search-label" htmlFor="incoming-call-search">جست‌وجوی تلفن، سفارش، مأموریت یا نام</label>
          <div className="incoming-search"><input id="incoming-call-search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="شماره، سفارش، مأموریت یا نام…" /><button type="button" onClick={() => setNotice(visibleMatches.length ? `${visibleMatches.length} نتیجهٔ نمونه پیدا شد.` : 'نتیجه‌ای برای این جست‌وجو پیدا نشد.')}>جست‌وجو</button></div>
          {query && <div className="incoming-search-results" aria-live="polite">{visibleMatches.length ? visibleMatches.map((item) => <button key={item.id} onClick={() => { setNotice(`مأموریت ${item.id} انتخاب شد.`); setQuery(item.id) }}><strong>{item.id}</strong><span>{item.branch} · {item.status}</span></button>) : <span>۰ نتیجه</span>}</div>}
        </section> : scenario === 'multiple' ? <section className="multiple-call-section">
          <div className="incoming-section-heading"><h3>۳ مأموریت فعالِ مرتبط با شماره</h3><span className="incoming-chip warning">انتخاب مأموریت</span></div>
          <div className="incoming-match-list">{matches.map((item, index) => <button key={item.id} className={`incoming-match-card ${selected === index ? 'selected' : ''}`} onClick={() => setSelected(index)} aria-pressed={selected === index}><strong>{item.id} <span>{item.branch}</span></strong><small>{item.status} · موعد {item.committed} · {item.risk} · {item.courier}</small></button>)}</div>
          <h3 className="incoming-selected-title">زمینهٔ مأموریت منتخب</h3>
          <MissionSummary mission={mission} />
        </section> : <section className="single-call-section">
          <div className="incoming-section-heading"><h3>سفارش فعال</h3><span className="incoming-chip warning">{mission.risk}</span></div>
          <MissionSummary mission={mission} />
          <div className="incoming-exception"><strong>استثنای باز • تأخیر آماده‌سازی شعبه</strong><p>انتظار از ۳ دقیقه گذشته است؛ SLA در خطر است. وضعیت و امکان لغو از رکورد معتبر مأموریت خوانده می‌شود.</p></div>
          <section className="incoming-cancel-state"><strong>وضعیت لغو</strong><p><b>مجاز</b><span>پیش از handoff فروشگاه به پیک</span></p></section>
        </section>}
        {notice && <div className="incoming-call-notice" role="status">{notice}</div>}
      </div>
      <footer className="incoming-call-actions">
        <div className="incoming-action-row"><button className="incoming-primary" onClick={() => onOpenMission(mission.id)} disabled={scenario === 'unmatched'}>{scenario === 'unmatched' ? 'جست‌وجوی مأموریت' : 'باز کردن مأموریت'}</button><button className="incoming-secondary" disabled title="پس از اتصال درگاه تلفنی فعال می‌شود">پاسخ به تماس · درگاه تلفنی</button></div>
        <div className="incoming-action-row"><button className="incoming-secondary" onClick={() => setNotice('درخواست ثبت نمی‌شود؛ API پشتیبانی هنوز به این پنل متصل نیست.')}>ثبت استثنای عملیاتی</button><button className="incoming-secondary" onClick={() => scenario === 'multiple' ? setSelected((selected + 1) % matches.length) : setNotice('این گزینه در اتصال تلفنی واقعی فعال می‌شود.')}>{scenario === 'multiple' ? 'مأموریت بعدی' : 'انتخاب مأموریت دیگر'}</button></div>
        <p>این پیش‌نمایش فقط دادهٔ نمونه دارد؛ تماس واقعی به اتصال تلفنی و تطبیق سمت سرور نیاز دارد.</p>
      </footer>
    </aside>
  </div>
}

function InfoRow({ label, value }: { label: string; value: string }) { return <div className="incoming-info-row"><span>{value}</span><small>{label}</small></div> }
function MissionSummary({ mission }: { mission: typeof matches[number] }) {
  return <div className="incoming-mission-summary"><InfoRow label="شناسهٔ مأموریت" value={mission.id} /><InfoRow label="شمارهٔ سفارش" value={mission.order} /><InfoRow label="وضعیت" value={mission.status} /><InfoRow label="زمان تعهدشده" value={mission.committed} /><InfoRow label="زمان تخمینی فعلی" value={mission.eta} /><InfoRow label="پیک / مسیر" value={`${mission.courier} • ${mission.route}`} /><InfoRow label="مبدأ" value={mission.branch} /><InfoRow label="مقصد" value={mission.destination} /><InfoRow label="آخرین رویداد" value={mission.event} /></div>
}
