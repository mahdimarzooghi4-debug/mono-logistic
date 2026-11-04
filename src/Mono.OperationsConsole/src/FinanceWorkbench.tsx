import { useState } from 'react'
import './finance-workbench.css'

export type MissionFare = {
  mission_id: string
  customer_id: string
  delivery_request_id: string
  status: string
  committed_delivery_at: string
  created_at: string
  quote_id: string
  quote_version: number
  distance_meters: number
  fare_toman: number
  buyer_share_toman: number
  seller_share_toman: number
  mono_commission_estimate_toman: number
  commission_vat_estimate_toman: number
  courier_net_estimate_toman: number
}

type Props = {
  items: MissionFare[]
  loading: boolean
  error: string
  token: string
  query: string
  status: string
  pageIndex: number
  hasNext: boolean
  onQueryChange: (value: string) => void
  onStatusChange: (value: string) => void
  onSearch: (status?: string) => void
  onPrevious: () => void
  onNext: () => void
}

const fa = new Intl.NumberFormat('fa-IR')
const money = (value?: number | null) => typeof value === 'number' && Number.isFinite(value) ? `${fa.format(value)} تومان` : '—'
const statusLabel: Record<string, string> = { DELIVERED: 'تحویل‌شده', FAILED: 'ناموفق', CANCELLED: 'لغوشده', CREATED: 'ثبت‌شده', ACCEPTED: 'در انتظار' }
const financePreviewItems: MissionFare[] = [
  { mission_id: 'MIS-10482', customer_id: 'حنا', delivery_request_id: 'RTE-07', status: 'DELIVERED', committed_delivery_at: '2026-11-01T12:00:00Z', created_at: '2026-11-01T09:00:00Z', quote_id: 'CR-204', quote_version: 1, distance_meters: 4800, fare_toman: 50000, buyer_share_toman: 50000, seller_share_toman: 0, mono_commission_estimate_toman: 5000, commission_vat_estimate_toman: 500, courier_net_estimate_toman: 44500 },
  { mission_id: 'MIS-10479', customer_id: 'حنا', delivery_request_id: 'RTE-08', status: 'ACCEPTED', committed_delivery_at: '2026-11-01T13:00:00Z', created_at: '2026-11-01T10:00:00Z', quote_id: 'CR-118', quote_version: 1, distance_meters: 4200, fare_toman: 42500, buyer_share_toman: 42500, seller_share_toman: 0, mono_commission_estimate_toman: 4250, commission_vat_estimate_toman: 425, courier_net_estimate_toman: 37825 },
  { mission_id: 'MIS-10474', customer_id: 'حنا', delivery_request_id: 'RTE-30', status: 'DELIVERED', committed_delivery_at: '2026-11-01T11:00:00Z', created_at: '2026-11-01T08:00:00Z', quote_id: 'CR-302', quote_version: 1, distance_meters: 3500, fare_toman: 35000, buyer_share_toman: 35000, seller_share_toman: 0, mono_commission_estimate_toman: 3500, commission_vat_estimate_toman: 350, courier_net_estimate_toman: 31150 },
  { mission_id: 'MIS-10461', customer_id: 'حنا', delivery_request_id: 'RTE-11', status: 'ACCEPTED', committed_delivery_at: '2026-11-01T14:00:00Z', created_at: '2026-11-01T10:30:00Z', quote_id: 'CR-311', quote_version: 1, distance_meters: 3700, fare_toman: 37000, buyer_share_toman: 37000, seller_share_toman: 0, mono_commission_estimate_toman: 3700, commission_vat_estimate_toman: 370, courier_net_estimate_toman: 32930 },
  { mission_id: 'MIS-10455', customer_id: 'حنا', delivery_request_id: 'RTE-25', status: 'DELIVERED', committed_delivery_at: '2026-11-01T10:00:00Z', created_at: '2026-11-01T07:30:00Z', quote_id: 'CR-225', quote_version: 1, distance_meters: 2000, fare_toman: 20000, buyer_share_toman: 20000, seller_share_toman: 0, mono_commission_estimate_toman: 2000, commission_vat_estimate_toman: 200, courier_net_estimate_toman: 17800 },
  { mission_id: 'MIS-10450', customer_id: 'حنا', delivery_request_id: 'RTE-14', status: 'DELIVERED', committed_delivery_at: '2026-11-01T09:00:00Z', created_at: '2026-11-01T06:30:00Z', quote_id: 'CR-174', quote_version: 1, distance_meters: 3100, fare_toman: 30000, buyer_share_toman: 30000, seller_share_toman: 0, mono_commission_estimate_toman: 3000, commission_vat_estimate_toman: 300, courier_net_estimate_toman: 26700 },
]

export default function FinanceWorkbench({ items, loading, error, token, query, status, pageIndex, hasNext, onQueryChange, onStatusChange, onSearch, onPrevious, onNext }: Props) {
  const [selectedId, setSelectedId] = useState('')
  const demoMode = !token && new URLSearchParams(window.location.search).get('demo') === '1'
  const visibleItems = demoMode ? financePreviewItems.filter((item) => (!status || item.status === status) && (!query.trim() || `${item.mission_id} ${item.delivery_request_id} ${item.quote_id}`.toLowerCase().includes(query.trim().toLowerCase()))) : items
  const selected = visibleItems.find((item) => item.mission_id === selectedId) || visibleItems[0] || null
  const totals = visibleItems.reduce((sum, item) => ({
    fare: sum.fare + item.fare_toman,
    commission: sum.commission + item.mono_commission_estimate_toman,
    vat: sum.vat + item.commission_vat_estimate_toman,
    courier: sum.courier + item.courier_net_estimate_toman,
  }), { fare: 0, commission: 0, vat: 0, courier: 0 })

  return <div className="finance-workbench" data-demo={demoMode}>
    <header className="finance-heading"><div><h1>مالی و تسویه</h1><p>کنترل درآمد، مالیات، سهم پیک‌ها، کیف پول و برداشت‌ها {demoMode && <span className="finance-demo-badge">دادهٔ نمایشی</span>}</p></div><div className="finance-actions"><button className="primary-button" disabled title="ثبت تعدیل مالی در API فعلی پشتیبانی نمی‌شود.">ثبت تعدیل</button><button className="secondary-button" disabled title="گزارش تجمیعی پرداخت‌ها در API فعلی موجود نیست.">گزارش مالی</button></div></header>
    <section className="finance-kpis" aria-label="خلاصهٔ مالی همین صفحه">
      <article><small>برداشت در انتظار</small><strong className="finance-pending-value">{demoMode ? '۳ مورد' : '—'}</strong></article>
      <article><small>خالص برآوردی پیک‌ها</small><strong>{(token || demoMode) && visibleItems.length ? money(demoMode ? 6666000 : totals.courier) : '—'}</strong></article>
      <article><small>مالیات ارزش افزوده پرداختی</small><strong>{(token || demoMode) && visibleItems.length ? money(demoMode ? 500 : totals.vat) : '—'}</strong></article>
      <article><small>درآمد کمیسیون مونو</small><strong>{(token || demoMode) && visibleItems.length ? money(demoMode ? 5000 : totals.commission) : '—'}</strong></article>
      <article><small>کرایهٔ امروز</small><strong>{(token || demoMode) && visibleItems.length ? money(demoMode ? 2480000 : totals.fare) : '—'}</strong></article>
    </section>
    <div className="finance-layout">
      <aside className="finance-mission-list panel-card">
        <header><h2>تسویهٔ مأموریت‌ها</h2><span>{token || demoMode ? fa.format(visibleItems.length) : '—'} مورد</span></header>
        <form className="finance-search" onSubmit={(event) => { event.preventDefault(); if (!demoMode) onSearch() }}>
          <input aria-label="جست‌وجوی مأموریت مالی" value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="جست‌وجوی مأموریت یا سفارش" />
          <button type="submit" disabled={(!token && !demoMode) || loading} aria-label="جست‌وجو">⌕</button>
        </form>
        <div className="finance-status-filters" aria-label="فیلتر وضعیت مأموریت">
          {[['', 'همه'], ['ACCEPTED', 'در انتظار'], ['DELIVERED', 'تسویه‌شده']].map(([value, label]) => <button key={value} type="button" className={status === value ? 'active' : ''} aria-pressed={status === value} onClick={() => { onStatusChange(value); if (!demoMode) onSearch(value) }} disabled={(!token && !demoMode) || loading}>{label}</button>)}
        </div>
        <div className="finance-mission-items">
          {visibleItems.map((item) => <button key={item.mission_id} type="button" className={`finance-mission-row ${selected?.mission_id === item.mission_id ? 'selected' : ''}`} onClick={() => setSelectedId(item.mission_id)}>
            <strong dir="ltr">{item.mission_id}</strong>
            <span className={`finance-status-chip ${item.status === 'DELIVERED' ? 'success' : item.status === 'FAILED' ? 'danger' : 'warning'}`}>{demoMode && item.status === 'DELIVERED' ? 'تسویه‌شده' : statusLabel[item.status] || item.status}</span>
            <small>{fa.format(item.distance_meters)} متر · {money(item.fare_toman)}</small>
            <small dir="ltr">{item.quote_id} · نسخهٔ {fa.format(item.quote_version)}</small>
          </button>)}
          {!visibleItems.length && <p className="finance-empty">{loading ? 'در حال دریافت مأموریت‌های مالی…' : token ? 'مأموریتی برای این فیلتر پیدا نشد.' : 'برای مشاهدهٔ دادهٔ واقعی، به API عملیات متصل شوید.'}</p>}
        </div>
        <footer><button className="secondary-button" onClick={onPrevious} disabled={!token || loading || pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(pageIndex + 1)}</span><button className="secondary-button" onClick={onNext} disabled={!token || loading || !hasNext}>بعدی</button></footer>
      </aside>
      <main className="finance-detail">
        {error && token && <div className="finance-error" role="alert">{error}</div>}
        <section className="finance-selected panel-card">
          <header><div className="finance-selected-copy"><div className="finance-selected-title"><h2>{selected ? `تسویهٔ مالی — ${selected.mission_id}` : 'مأموریت مالی را انتخاب کنید'}</h2><span className={`finance-status-chip ${selected?.status === 'DELIVERED' ? 'success' : selected?.status === 'FAILED' ? 'danger' : 'warning'}`}>{selected ? (demoMode ? 'تسویه‌شده' : statusLabel[selected.status] || selected.status) : 'بدون انتخاب'}</span></div><p>{selected ? demoMode ? `مشتری: حنا  •  پیک: ${selected.quote_id}  •  مسیر: ${selected.delivery_request_id}  •  کرایهٔ سرویس: ${money(selected.fare_toman)}` : 'مقادیر زیر از snapshot قطعی کرایه خوانده شده‌اند؛ پرداخت و تسویهٔ واقعی در API گزارش نمی‌شوند.' : 'پس از اتصال به API، مأموریت و برآوردهای ثبت‌شده نمایش داده می‌شود.'}</p></div></header>
          <div className="finance-facts"><div><small>کمیسیون مونو</small><strong>{selected ? money(selected.mono_commission_estimate_toman) : '—'}</strong></div><div><small>مالیات کمیسیون</small><strong>{selected ? money(selected.commission_vat_estimate_toman) : '—'}</strong></div><div><small>سهم فروشنده</small><strong>{selected ? money(selected.seller_share_toman) : '—'}</strong></div><div><small>سهم خریدار</small><strong>{selected ? money(selected.buyer_share_toman) : '—'}</strong></div><div><small>خالص برآوردی پیک</small><strong>{selected ? money(selected.courier_net_estimate_toman) : '—'}</strong></div></div>
        </section>
        <div className="finance-card-grid">
          <section className="panel-card finance-ledger"><header><h3>دفتر مالی مأموریت</h3><span>{demoMode ? 'امروز' : `snapshot نرخ ${selected ? `نسخهٔ ${fa.format(selected.quote_version)}` : '—'}`}</span></header><dl>
            {demoMode ? <>
              <div><dt>حساب دریافتی حنا</dt><dd className="finance-positive">۵۰,۰۰۰</dd><small>بدهکار</small></div>
              <div><dt>درآمد کمیسیون مونو</dt><dd className="finance-positive">۵,۰۰۰</dd><small>بستانکار</small></div>
              <div><dt>مالیات ارزش افزوده پرداختی</dt><dd className="finance-positive">۵۰۰</dd><small>بستانکار</small></div>
              <div><dt>بستانکار پیک</dt><dd className="finance-negative">۴۴,۵۰۰</dd><small>بستانکار</small></div>
            </> : <>
              <div><dt>کرایهٔ ثبت‌شده</dt><dd>{selected ? money(selected.fare_toman) : '—'}</dd></div>
              <div><dt>سهم خریدار</dt><dd>{selected ? money(selected.buyer_share_toman) : '—'}</dd></div>
              <div><dt>سهم فروشنده</dt><dd>{selected ? money(selected.seller_share_toman) : '—'}</dd></div>
              <div><dt>کمیسیون مونو (برآورد)</dt><dd>{selected ? money(selected.mono_commission_estimate_toman) : '—'}</dd></div>
              <div><dt>مالیات کمیسیون (برآورد)</dt><dd>{selected ? money(selected.commission_vat_estimate_toman) : '—'}</dd></div>
            </>}
          </dl>{demoMode && <p className="finance-ledger-note">سند متوازن است؛ ثبت نهایی فقط پس از تأیید عملیات انجام می‌شود.</p>}</section>
          <section className="panel-card finance-invoice"><header><h3>صورت‌حساب حنا</h3><span className="finance-status-chip neutral">{demoMode ? 'ماهانه' : 'فقط برآورد'}</span></header><dl>
            {demoMode ? <>
              <div><dt>چرخهٔ صورتحساب</dt><dd>ماهانه</dd></div>
              <div><dt>مأموریت امروز</dt><dd>۲۴۸</dd></div>
              <div><dt>کرایهٔ امروز</dt><dd>۲,۴۸۰,۰۰۰ تومان</dd></div>
              <div><dt>وضعیت اعتبار</dt><dd className="finance-positive">سالم</dd></div>
              <div><dt>بدهی معوق</dt><dd className="finance-positive">ندارد</dd></div>
            </> : <>
              <div><dt>مشتری</dt><dd dir="ltr">{selected?.customer_id || '—'}</dd></div>
              <div><dt>درخواست سفارش</dt><dd dir="ltr">{selected?.delivery_request_id || '—'}</dd></div>
              <div><dt>شناسهٔ نرخ</dt><dd dir="ltr">{selected?.quote_id || '—'}</dd></div>
              <div><dt>کرایهٔ سفارش</dt><dd>{selected ? money(selected.fare_toman) : '—'}</dd></div>
              <div><dt>وضعیت صورتحساب</dt><dd>در API موجود نیست</dd></div>
            </>}
            </dl>{demoMode ? <button className="secondary-button finance-invoice-action" disabled>مشاهدهٔ صورتحساب</button> : <p>دادهٔ وصول از حنا یا وضعیت صورتحساب در این API نیست.</p>}</section>
          <section className="panel-card finance-wallet"><header><h3>کیف پول پیک</h3><span className="finance-status-chip neutral">{demoMode ? selected?.quote_id : 'بدون دادهٔ کیف پول'}</span></header><dl>
            {demoMode ? <>
              <div><dt>موجودی قابل برداشت</dt><dd className="finance-positive">۸۷۲,۴۰۰ تومان</dd></div>
              <div><dt>موجودی در انتظار</dt><dd className="finance-warning">۱۷۹,۰۰۰ تومان</dd></div>
              <div><dt>برداشت امروز</dt><dd>۳ از ۳</dd></div>
              <div><dt>برداشت باقی‌مانده</dt><dd>۱</dd></div>
              <div><dt>شبا</dt><dd dir="ltr">IR•••• ۴۸۱۲</dd></div>
            </> : <>
              <div><dt>خالص برآوردی این مأموریت</dt><dd>{selected ? money(selected.courier_net_estimate_toman) : '—'}</dd></div>
              <div><dt>موجودی قابل برداشت</dt><dd>—</dd></div>
              <div><dt>پرداخت‌شده</dt><dd>—</dd></div>
              <div><dt>ماندهٔ کیف پول</dt><dd>—</dd></div>
            </>}
          </dl>{demoMode ? <div className="finance-last-transaction"><strong>آخرین تراکنش</strong><span>MIS-۱۰۴۸۲ · برداشت ۲۵۰,۰۰۰ تومان</span></div> : <p>برآورد خالص مأموریت، موجودی یا حق برداشت پیک را نشان نمی‌دهد.</p>}</section>
          <section className="panel-card finance-payouts"><header><h3>برداشت‌ها</h3><span>{demoMode ? '۳ مورد ثبت‌شده' : '— مورد ثبت‌شده'}</span></header>{demoMode ? <div className="finance-payout-list">
            {[['WD-۲۴۱۹', '۳۵۰,۰۰۰ تومان'], ['WD-۲۴۱۷', '۲۵۰,۰۰۰ تومان'], ['WD-۲۴۱۳', '۴۰۰,۰۰۰ تومان']].map(([id, amount]) => <div className="finance-payout-row" key={id}><strong>{id}</strong><b>{amount}</b><span className="finance-status-chip success">موفق</span></div>)}
          </div> : <div className="finance-payout-empty">تاریخچهٔ پرداخت در API مالی موجود نیست.</div>}<button className="secondary-button" disabled title="ثبت پرداخت در API فعلی ارائه نشده است.">ثبت برداشت</button><button className="secondary-button" disabled title="تطبیق پرداخت در API فعلی ارائه نشده است.">بازبینی تطبیق</button></section>
        </div>
        <p className="finance-data-note">{demoMode ? 'داده‌های این پیش‌نمایش ساختگی‌اند و فقط برای مقایسهٔ ظاهر با فیگما نمایش داده می‌شوند.' : 'این صفحه فقط snapshot کرایه و برآورد سهم‌ها را نشان می‌دهد؛ موجودی کیف پول، فاکتور، پرداخت، برداشت و تسویهٔ واقعی از API فعلی قابل دریافت یا تغییر نیست.'}</p>
      </main>
    </div>
  </div>
}
