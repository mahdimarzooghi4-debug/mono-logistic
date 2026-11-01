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
  onRefresh: () => void
}

const fa = new Intl.NumberFormat('fa-IR')
const money = (value?: number | null) => typeof value === 'number' && Number.isFinite(value) ? `${fa.format(value)} تومان` : '—'
const statusLabel: Record<string, string> = { DELIVERED: 'تحویل‌شده', FAILED: 'ناموفق', CANCELLED: 'لغوشده', CREATED: 'ثبت‌شده', ACCEPTED: 'پذیرفته‌شده' }

export default function FinanceWorkbench({ items, loading, error, token, query, status, pageIndex, hasNext, onQueryChange, onStatusChange, onSearch, onPrevious, onNext, onRefresh }: Props) {
  const [selectedId, setSelectedId] = useState('')
  const selected = items.find((item) => item.mission_id === selectedId) || items[0] || null
  const totals = items.reduce((sum, item) => ({
    fare: sum.fare + item.fare_toman,
    commission: sum.commission + item.mono_commission_estimate_toman,
    vat: sum.vat + item.commission_vat_estimate_toman,
    courier: sum.courier + item.courier_net_estimate_toman,
  }), { fare: 0, commission: 0, vat: 0, courier: 0 })

  return <div className="finance-workbench">
    <header className="finance-heading"><div><h1>مالی و تسویه</h1><p>بازبینی سهم‌های ثبت‌شده و برآورد مالی مأموریت‌ها</p></div><div className="finance-actions"><button className="secondary-button" disabled title="گزارش تجمیعی پرداخت‌ها در API فعلی موجود نیست.">گزارش مالی</button><button className="primary-button" disabled title="ثبت تعدیل مالی در API فعلی پشتیبانی نمی‌شود.">ثبت تعدیل</button></div></header>
    <section className="finance-kpis" aria-label="خلاصهٔ مالی همین صفحه">
      <article><small>پرداخت در انتظار</small><strong>—</strong></article>
      <article><small>خالص برآوردی پیک‌ها</small><strong>{token && items.length ? money(totals.courier) : '—'}</strong></article>
      <article><small>مالیات کمیسیون</small><strong>{token && items.length ? money(totals.vat) : '—'}</strong></article>
      <article><small>ناخالص کرایه</small><strong>{token && items.length ? money(totals.fare) : '—'}</strong></article>
      <article><small>کمیسیون مونو</small><strong>{token && items.length ? money(totals.commission) : '—'}</strong></article>
    </section>
    <div className="finance-layout">
      <aside className="finance-mission-list panel-card">
        <header><h2>تسویهٔ مأموریت‌ها</h2><span>{token ? fa.format(items.length) : '—'} مورد</span></header>
        <form className="finance-search" onSubmit={(event) => { event.preventDefault(); onSearch() }}>
          <input aria-label="جست‌وجوی مأموریت مالی" value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="جست‌وجوی مأموریت یا سفارش" />
          <button type="submit" disabled={!token || loading} aria-label="جست‌وجو">⌕</button>
        </form>
        <div className="finance-status-filters" aria-label="فیلتر وضعیت مأموریت">
          {[['', 'همه'], ['DELIVERED', 'تحویل‌شده'], ['FAILED', 'ناموفق']].map(([value, label]) => <button key={value} type="button" className={status === value ? 'active' : ''} aria-pressed={status === value} onClick={() => { onStatusChange(value); onSearch(value) }} disabled={!token || loading}>{label}</button>)}
        </div>
        <div className="finance-mission-items">
          {items.map((item) => <button key={item.mission_id} type="button" className={`finance-mission-row ${selected?.mission_id === item.mission_id ? 'selected' : ''}`} onClick={() => setSelectedId(item.mission_id)}>
            <span className={`finance-status-chip ${item.status === 'DELIVERED' ? 'success' : item.status === 'FAILED' ? 'danger' : 'warning'}`}>{statusLabel[item.status] || item.status}</span>
            <strong dir="ltr">{item.mission_id}</strong>
            <small>{fa.format(item.distance_meters)} متر · {money(item.fare_toman)}</small>
            <small dir="ltr">{item.quote_id} · نسخهٔ {fa.format(item.quote_version)}</small>
          </button>)}
          {!items.length && <p className="finance-empty">{loading ? 'در حال دریافت مأموریت‌های مالی…' : token ? 'مأموریتی برای این فیلتر پیدا نشد.' : 'برای مشاهدهٔ دادهٔ واقعی، به API عملیات متصل شوید.'}</p>}
        </div>
        <footer><button className="secondary-button" onClick={onPrevious} disabled={!token || loading || pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(pageIndex + 1)}</span><button className="secondary-button" onClick={onNext} disabled={!token || loading || !hasNext}>بعدی</button></footer>
      </aside>
      <main className="finance-detail">
        {error && token && <div className="finance-error" role="alert">{error}</div>}
        <section className="finance-selected panel-card">
          <header><div><span className={`finance-status-chip ${selected?.status === 'DELIVERED' ? 'success' : selected?.status === 'FAILED' ? 'danger' : 'warning'}`}>{selected ? statusLabel[selected.status] || selected.status : 'بدون انتخاب'}</span><h2>{selected ? `تسویهٔ مالی — ${selected.mission_id}` : 'مأموریت مالی را انتخاب کنید'}</h2><p>{selected ? 'مقادیر زیر از snapshot قطعی کرایه خوانده شده‌اند؛ پرداخت و تسویهٔ واقعی در API گزارش نمی‌شوند.' : 'پس از اتصال به API، مأموریت و برآوردهای ثبت‌شده نمایش داده می‌شود.'}</p></div><button className="icon-button" onClick={onRefresh} disabled={!token || loading} aria-label="تازه‌سازی">↻</button></header>
          <div className="finance-facts"><div><small>کمیسیون مونو</small><strong>{selected ? money(selected.mono_commission_estimate_toman) : '—'}</strong></div><div><small>مالیات کمیسیون</small><strong>{selected ? money(selected.commission_vat_estimate_toman) : '—'}</strong></div><div><small>سهم فروشنده</small><strong>{selected ? money(selected.seller_share_toman) : '—'}</strong></div><div><small>سهم خریدار</small><strong>{selected ? money(selected.buyer_share_toman) : '—'}</strong></div><div><small>خالص برآوردی پیک</small><strong>{selected ? money(selected.courier_net_estimate_toman) : '—'}</strong></div></div>
        </section>
        <div className="finance-card-grid">
          <section className="panel-card finance-ledger"><header><h3>دفتر مالی مأموریت</h3><span>snapshot نرخ {selected ? `نسخهٔ ${fa.format(selected.quote_version)}` : '—'}</span></header><dl>
            <div><dt>کرایهٔ ثبت‌شده</dt><dd>{selected ? money(selected.fare_toman) : '—'}</dd></div>
            <div><dt>سهم خریدار</dt><dd>{selected ? money(selected.buyer_share_toman) : '—'}</dd></div>
            <div><dt>سهم فروشنده</dt><dd>{selected ? money(selected.seller_share_toman) : '—'}</dd></div>
            <div><dt>کمیسیون مونو (برآورد)</dt><dd>{selected ? money(selected.mono_commission_estimate_toman) : '—'}</dd></div>
            <div><dt>مالیات کمیسیون (برآورد)</dt><dd>{selected ? money(selected.commission_vat_estimate_toman) : '—'}</dd></div>
          </dl></section>
          <section className="panel-card finance-invoice"><header><h3>صورت‌حساب حنا</h3><span className="finance-status-chip neutral">فقط برآورد</span></header><dl>
            <div><dt>مشتری</dt><dd dir="ltr">{selected?.customer_id || '—'}</dd></div>
            <div><dt>درخواست سفارش</dt><dd dir="ltr">{selected?.delivery_request_id || '—'}</dd></div>
            <div><dt>شناسهٔ نرخ</dt><dd dir="ltr">{selected?.quote_id || '—'}</dd></div>
            <div><dt>کرایهٔ سفارش</dt><dd>{selected ? money(selected.fare_toman) : '—'}</dd></div>
            <div><dt>وضعیت صورتحساب</dt><dd>در API موجود نیست</dd></div>
          </dl><p>دادهٔ وصول از حنا یا وضعیت صورتحساب در این API نیست.</p></section>
          <section className="panel-card finance-wallet"><header><h3>کیف پول پیک</h3><span className="finance-status-chip neutral">بدون دادهٔ کیف پول</span></header><dl>
            <div><dt>خالص برآوردی این مأموریت</dt><dd>{selected ? money(selected.courier_net_estimate_toman) : '—'}</dd></div>
            <div><dt>موجودی قابل برداشت</dt><dd>—</dd></div>
            <div><dt>پرداخت‌شده</dt><dd>—</dd></div>
            <div><dt>ماندهٔ کیف پول</dt><dd>—</dd></div>
          </dl><p>برآورد خالص مأموریت، موجودی یا حق برداشت پیک را نشان نمی‌دهد.</p></section>
          <section className="panel-card finance-payouts"><header><h3>پرداخت‌ها</h3><span>— مورد ثبت‌شده</span></header><div className="finance-payout-empty">تاریخچهٔ پرداخت در API مالی موجود نیست.</div><button className="secondary-button" disabled title="ثبت پرداخت در API فعلی ارائه نشده است.">ثبت پرداخت</button><button className="secondary-button" disabled title="تطبیق پرداخت در API فعلی ارائه نشده است.">بازبینی تطبیق</button></section>
        </div>
        <p className="finance-data-note">این صفحه فقط snapshot کرایه و برآورد سهم‌ها را نشان می‌دهد؛ موجودی کیف پول، فاکتور، پرداخت، برداشت و تسویهٔ واقعی از API فعلی قابل دریافت یا تغییر نیست.</p>
      </main>
    </div>
  </div>
}
