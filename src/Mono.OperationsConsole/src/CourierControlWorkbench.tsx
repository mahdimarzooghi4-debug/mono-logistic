import type { FormEvent } from 'react'
import './courier-control-workbench.css'

type Courier = { courier_id: string; mode: string; status: string; preferred_zone_ids: string[]; approved_zone_ids: string[]; primary_zone_id: string | null; created_at: string; updated_at: string }
type CourierDetail = Courier & { created_by: string; trust_transitions: Array<{ transition_id: string; from_status: string; to_status: string; actor_id: string; reason_code: string; evidence_reference: string; occurred_at: string }> }
type CourierAudit = { audit_id: string; actor_id: string; action: string; courier_id: string | null; result_count: number; occurred_at: string }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
function formatDate(value?: string | null) { if (!value) return '—'; const date = new Date(value); return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date) }
function trustLabel(status: string) { const labels: Record<string, string> = { REGISTERED: 'ثبت‌شده', IDENTITY_VERIFIED: 'هویت تأییدشده', DOCUMENTS_SUBMITTED: 'مدارک ارسال‌شده', TRUST_REQUIREMENTS_VERIFIED: 'شرایط اعتماد تأییدشده', MONO_APPROVED: 'تأییدشده توسط مونو', ACTIVE: 'فعال', SUSPENDED: 'معلق' }; return labels[status] || status }
function modeLabel(mode: string) { const labels: Record<string, string> = { BIKE: 'موتورسیکلت', MOTORCYCLE: 'موتورسیکلت', BICYCLE: 'دوچرخه', WALK: 'پیاده', CAR: 'خودرو' }; return labels[mode] || mode }

type Props = {
  couriers: Courier[]; selectedCourierId: string; detail: CourierDetail | null; audit: CourierAudit[]
  loading: boolean; detailLoading: boolean; detailErrors: string[]; error: string
  query: string; status: string; token: string; pageIndex: number; hasNext: boolean
  onQueryChange: (value: string) => void; onStatusChange: (value: string) => void
  onSearch: (event: FormEvent<HTMLFormElement>) => void; onSelect: (courierId: string) => void
  onPrevious: () => void; onNext: () => void; onOpenAdmissions: () => void; onOpenZones: () => void; onRefresh: () => void
}

export default function CourierControlWorkbench(props: Props) {
  const active = props.couriers.filter((courier) => courier.status === 'ACTIVE').length
  const selected = props.couriers.find((courier) => courier.courier_id === props.selectedCourierId)
  return <section className="courier-control-workbench" dir="rtl">
    <header className="courier-control-header">
      <div><h2>کنترل پیک‌ها</h2><p>وضعیت اعتماد، تخصیص، محدوده و آمادگی عملیاتی پیک‌ها</p></div>
      <div className="courier-control-actions"><button className="secondary-button" onClick={props.onRefresh} disabled={!props.token || props.loading}>تازه‌سازی</button><button className="secondary-button" onClick={props.onOpenZones}>مدیریت مناطق</button><button className="primary-button" onClick={props.onOpenAdmissions}>افزودن پیک</button></div>
    </header>

    <section className="courier-control-kpis" aria-label="خلاصهٔ پیک‌های همین صفحه">
      <article><span>فعال در این صفحه</span><strong>{props.token ? fa.format(active) : '—'}</strong><small>بر اساس وضعیت اعتماد API</small></article>
      <article><span>در مسیر فعال</span><strong>—</strong><small>مأموریت جاری از API پیک موجود نیست</small></article>
      <article><span>در محل دریافت</span><strong>—</strong><small>رویداد دریافت از API پیک موجود نیست</small></article>
      <article><span>مدارک نزدیک انقضا</span><strong>—</strong><small>تاریخ انقضای مدرک برگردانده نمی‌شود</small></article>
      <article><span>غیرفعال / خارج از دسترس</span><strong>—</strong><small>وضعیت حضور از API فهرست پیک‌ها نمی‌آید</small></article>
    </section>

    <div className="courier-control-layout">
      <aside className="courier-directory queue-panel">
        <header><div><h3>پیک‌ها</h3><span className="courier-count-pill">{props.token ? `${fa.format(props.couriers.length)} در این صفحه` : 'دادهٔ API'}</span></div></header>
        <form className="courier-directory-search" onSubmit={props.onSearch}>
          <label className="sr-only" htmlFor="courier-search">جست‌وجوی پیک</label>
          <input id="courier-search" value={props.query} onChange={(event) => props.onQueryChange(event.target.value)} placeholder="جست‌وجو با پیشوند شناسهٔ پیک" dir="ltr" autoComplete="off" />
          <label className="sr-only" htmlFor="courier-trust-filter">وضعیت اعتماد</label>
          <select id="courier-trust-filter" value={props.status} onChange={(event) => props.onStatusChange(event.target.value)}><option value="">همهٔ وضعیت‌ها</option><option value="REGISTERED">ثبت‌شده</option><option value="IDENTITY_VERIFIED">هویت تأییدشده</option><option value="DOCUMENTS_SUBMITTED">مدارک ارسال‌شده</option><option value="TRUST_REQUIREMENTS_VERIFIED">شرایط اعتماد تأییدشده</option><option value="MONO_APPROVED">تأییدشده توسط مونو</option><option value="ACTIVE">فعال</option></select>
          <button className="secondary-button" type="submit" disabled={!props.token || props.loading}>جست‌وجو</button>
        </form>
        {props.error && <p className="courier-directory-error" role="alert">{props.error}</p>}
        <div className="courier-directory-list" aria-label="نتایج پیک‌ها">
          {props.loading && <p className="courier-directory-empty"><span className="spinner"/>در حال دریافت پیک‌ها…</p>}
          {!props.loading && !props.couriers.length && <p className="courier-directory-empty">{props.token ? 'پیکی با این فیلتر در این صفحه پیدا نشد.' : 'برای مشاهدهٔ فهرست پیک‌ها به API عملیات متصل شوید.'}</p>}
          {props.couriers.map((courier) => <button key={courier.courier_id} className={`courier-directory-row${props.selectedCourierId === courier.courier_id ? ' selected' : ''}`} onClick={() => props.onSelect(courier.courier_id)} aria-pressed={props.selectedCourierId === courier.courier_id}>
            <span className={`courier-trust-pill ${courier.status === 'ACTIVE' ? 'active' : 'pending'}`}>{trustLabel(courier.status)}</span>
            <strong dir="ltr">{courier.courier_id}</strong><small>{modeLabel(courier.mode)} · {courier.primary_zone_id || 'زون اصلی ثبت نشده'}</small>
            <span className="courier-route-hint">مأموریت فعال از API دریافت نمی‌شود</span>
          </button>)}
        </div>
        <footer className="courier-directory-pagination"><button onClick={props.onPrevious} disabled={!props.token || props.loading || props.pageIndex === 0}>قبلی</button><span>صفحهٔ {fa.format(props.pageIndex + 1)}</span><button onClick={props.onNext} disabled={!props.token || props.loading || !props.hasNext}>بعدی</button></footer>
      </aside>

      <section className="courier-profile queue-panel">
        {selected ? <>
          <header className="courier-profile-header"><div><h3>{props.detail?.courier_id || selected.courier_id}</h3><p>{modeLabel(selected.mode)} · وضعیت اعتماد: {trustLabel(selected.status)} · آخرین تغییر {formatDate(selected.updated_at)}</p></div><span className={`courier-trust-pill ${selected.status === 'ACTIVE' ? 'active' : 'pending'}`}>{trustLabel(selected.status)}</span></header>
          {props.detailErrors.length > 0 && <div className="courier-detail-errors" role="alert">{props.detailErrors.map((message) => <p key={message}>{message}</p>)}</div>}
          {props.detailLoading ? <p className="courier-detail-loading"><span className="spinner"/>در حال دریافت جزئیات و سوابق…</p> : <>
            <div className="courier-profile-overview">
              <section className="courier-map-empty"><span>⌖</span><strong>موقعیت زنده در API پیک موجود نیست</strong><p>این صفحه مختصات GPS یا مسیر زندهٔ پیک را دریافت نمی‌کند.</p><small>زون حضور در پنل «حضور پیک‌ها» جداگانه نمایش داده می‌شود.</small></section>
              <section className="courier-current-job"><h4>کار جاری</h4><dl>
                <div><dt>مأموریت فعال</dt><dd>از API دریافت نمی‌شود</dd></div><div><dt>مسیر فعال</dt><dd>از API دریافت نمی‌شود</dd></div><div><dt>مبدأ / مقصد</dt><dd>از API دریافت نمی‌شود</dd></div><div><dt>شیفت و ظرفیت بار</dt><dd>از API دریافت نمی‌شود</dd></div>
              </dl><button disabled title="API پیک، مسیر قابل بازکردن ارائه نمی‌کند">باز کردن مسیر</button></section>
            </div>
            <div className="courier-profile-lower">
              <section className="courier-trust-card"><header><h4>صلاحیت، اعتماد و محدوده</h4><span className="courier-trust-pill active">{props.detail?.status || selected.status}</span></header><dl>
                <div><dt>وضعیت اعتماد</dt><dd>{trustLabel(props.detail?.status || selected.status)}</dd></div><div><dt>نوع عملیات</dt><dd>{modeLabel(props.detail?.mode || selected.mode)}</dd></div><div><dt>زون اصلی</dt><dd dir="ltr">{props.detail?.primary_zone_id || selected.primary_zone_id || 'ثبت نشده'}</dd></div><div><dt>زون‌های مجاز</dt><dd dir="ltr">{props.detail?.approved_zone_ids.join('، ') || selected.approved_zone_ids.join('، ') || 'ثبت نشده'}</dd></div><div><dt>زون‌های ترجیحی</dt><dd dir="ltr">{props.detail?.preferred_zone_ids.join('، ') || selected.preferred_zone_ids.join('، ') || 'ثبت نشده'}</dd></div><div><dt>ثبت‌کننده</dt><dd dir="ltr">{props.detail?.created_by || '—'}</dd></div>
              </dl><p className="courier-policy-note">تاریخ اعتبار مدارک، وضعیت GPS و قواعد احراز صلاحیت مأموریت در API فعلی موجود نیست.</p></section>
              <section className="courier-operations-card"><h4>عملکرد و کنترل عملیاتی</h4><div className="courier-metrics"><article><small>پذیرش</small><strong>—</strong></article><article><small>تحویل به‌موقع</small><strong>—</strong></article><article><small>لغو پیک</small><strong>—</strong></article><article><small>میانگین دریافت</small><strong>—</strong></article></div><div className="courier-operational-state"><strong>وضعیت عملیاتی</strong><p>Incident باز، محدودیت موقت و سلامت موقعیت در API پیک گزارش نمی‌شود.</p></div>
                <div className="courier-action-grid"><button disabled title="API تماس با پیک را ارائه نمی‌کند">تماس با پیک</button><button disabled title="API ارسال پیام به پیک را ارائه نمی‌کند">ارسال پیام</button><button disabled title="مدارک قابل‌نمایش در پاسخ API نیستند">مشاهده مدارک</button><button className="restrict-action" disabled title="عملیات محدودسازی موقت پیک در API این پنل موجود نیست">محدودسازی موقت</button></div>
              </section>
            </div>
            <details className="courier-audit-disclosure"><summary>سوابق اعتماد و دسترسی</summary><div className="courier-history-columns"><section><h4>تغییرات وضعیت اعتماد</h4>{props.detail?.trust_transitions.length ? <ol className="courier-transition-list">{props.detail.trust_transitions.map((event) => <li key={event.transition_id}><strong>{event.from_status} ← {event.to_status}</strong><small>{formatDate(event.occurred_at)} · {event.actor_id} · {event.reason_code}</small></li>)}</ol> : <p>تغییری در وضعیت اعتماد ثبت نشده است.</p>}</section><section><h4>سوابق خواندن پیک</h4>{props.audit.length ? <ul>{props.audit.map((event) => <li key={event.audit_id}>{event.action} · {event.actor_id} · {formatDate(event.occurred_at)}</li>)}</ul> : <p>سابقه‌ای برای نمایش برنگشت.</p>}</section></div></details>
          </>}
        </> : <div className="courier-profile-empty"><span>⌖</span><h3>یک پیک را از فهرست انتخاب کنید</h3><p>اطلاعات واقعی پیک، محدوده‌های ثبت‌شده و سوابق اعتماد پس از انتخاب نمایش داده می‌شود.</p></div>}
      </section>
    </div>
  </section>
}
