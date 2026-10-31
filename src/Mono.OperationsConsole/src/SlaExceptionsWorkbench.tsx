import { useCallback, useEffect, useMemo, useState } from 'react'
import './sla-exceptions-workbench.css'

type Alert = { mission_id: string; customer_id: string; status: string; committed_delivery_at: string; courier_id: string | null; is_overdue: boolean }
type AlertPage = { items: Alert[]; next_cursor: string | null; evaluated_at: string }
type Timeline = { events?: Array<{ eventId: string; eventType: string; sequence: number; occurredAt: string }> }
type Props = { token: string; onConnected: (connected: boolean) => void; onOpenMission: (missionId: string) => void }

const fa = new Intl.NumberFormat('fa-IR')
const fmt = (value: string) => new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
const humanStatus: Record<string, string> = { CREATED: 'ثبت‌شده', ACCEPTED: 'پذیرفته‌شده', PICKED_UP: 'تحویل از مبدأ', DELIVERED: 'تحویل‌شده', FAILED: 'ناموفق', CANCELLED: 'لغوشده', ASSIGNED: 'تخصیص‌یافته' }
const eventLabel: Record<string, string> = { MISSION_CREATED: 'ثبت مأموریت', COURIER_OFFER_ACCEPTED: 'پذیرش مأموریت', PICKED_UP: 'دریافت از مبدأ', DELIVERY_COMPLETED: 'تحویل مأموریت', MISSION_STATUS_CHANGED: 'تغییر وضعیت', COURIER_ASSIGNED: 'تخصیص پیک' }

export default function SlaExceptionsWorkbench({ token, onConnected, onOpenMission }: Props) {
  const [items, setItems] = useState<Alert[]>([])
  const [evaluatedAt, setEvaluatedAt] = useState('')
  const [cursor, setCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [horizon, setHorizon] = useState('0')
  const [selectedId, setSelectedId] = useState('')
  const [search, setSearch] = useState('')
  const [timeline, setTimeline] = useState<Timeline['events']>(undefined)
  const [detail, setDetail] = useState<{ missionId: string; customerId: string; status: string; committedDeliveryAt: string; createdAt: string } | null>(null)
  const [loading, setLoading] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [error, setError] = useState('')
  const [detailError, setDetailError] = useState('')

  const selected = items.find((item) => item.mission_id === selectedId) || null
  const visibleItems = useMemo(() => items.filter((item) => `${item.mission_id} ${item.customer_id}`.toLocaleLowerCase().includes(search.toLocaleLowerCase())), [items, search])
  const dueSoon = items.filter((item) => !item.is_overdue && new Date(item.committed_delivery_at).getTime() - new Date(evaluatedAt).getTime() <= Number(horizon) * 60000).length
  const load = useCallback(async (nextCursor: string | null = null, stack: Array<string | null> = [null]) => {
    if (!token) { setItems([]); setError('برای مشاهدهٔ هشدارهای واقعی، توکن مدیر عملیات را وارد کنید.'); return }
    setLoading(true); setError('')
    try {
      const params = new URLSearchParams({ limit: '50', minutes_ahead: horizon })
      if (nextCursor) params.set('cursor', nextCursor)
      const response = await fetch(`/operations/v1/sla/delivery-alerts?${params}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await response.text())
      const page = await response.json() as AlertPage
      if (!Array.isArray(page.items) || typeof page.evaluated_at !== 'string') throw new Error('ساختار پاسخ SLA با قرارداد API سازگار نیست.')
      setItems(page.items); setCursor(page.next_cursor || null); setCursorStack(stack); setEvaluatedAt(page.evaluated_at)
      setSelectedId((current) => page.items.some((item) => item.mission_id === current) ? current : page.items[0]?.mission_id || '')
      onConnected(true)
    } catch (cause) { setItems([]); setError(cause instanceof Error ? cause.message : 'دریافت هشدارهای SLA ناموفق بود.'); onConnected(false) }
    finally { setLoading(false) }
  }, [token, horizon, onConnected])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    if (!token || !selected) { setTimeline(undefined); setDetail(null); return }
    let cancelled = false
    setDetailLoading(true); setDetailError(''); setTimeline(undefined); setDetail(null)
    Promise.allSettled([
      fetch(`/operations/v1/missions/${encodeURIComponent(selected.mission_id)}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
      fetch(`/operations/v1/missions/${encodeURIComponent(selected.mission_id)}/timeline`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' }),
    ]).then(async ([missionResult, timelineResult]) => {
      const errors: string[] = []
      let mission: SlaExceptionsWorkbenchMission | null = null
      let events: Timeline['events']
      if (missionResult.status === 'fulfilled' && missionResult.value.ok) mission = await missionResult.value.json() as SlaExceptionsWorkbenchMission
      else errors.push('جزئیات مأموریت دریافت نشد.')
      if (timelineResult.status === 'fulfilled' && timelineResult.value.ok) { const data = await timelineResult.value.json() as Timeline; events = data.events }
      else errors.push('تاریخچهٔ مأموریت دریافت نشد.')
      if (cancelled) return
      setDetail(mission ? { missionId: mission.missionId, customerId: mission.customerId, status: mission.status, committedDeliveryAt: mission.committedDeliveryAt, createdAt: mission.createdAt } : null)
      setTimeline(events); setDetailError(errors.join(' '))
    }).catch(() => { if (!cancelled) setDetailError('دریافت جزئیات مأموریت ناموفق بود.') }).finally(() => { if (!cancelled) setDetailLoading(false) })
    return () => { cancelled = true }
  }, [token, selected?.mission_id])

  const timelineSorted = useMemo(() => [...(timeline || [])].sort((a, b) => a.sequence - b.sequence), [timeline])
  const overdueCount = items.filter((item) => item.is_overdue).length
  const unassignedCount = items.filter((item) => !item.courier_id).length

  return <div className="sla-workbench">
    <header className="sla-page-heading"><div><h1>استاندارد خدمت و استثناها</h1><p>رسیدگی به مأموریت‌های عبورکرده یا نزدیک موعد براساس ارزیابی ثبت‌شدهٔ API</p></div><div className="sla-heading-actions"><label>افق پایش<select aria-label="افق پایش" value={horizon} onChange={(event) => setHorizon(event.target.value)}><option value="0">موعد رسیده یا گذشته</option><option value="15">۱۵ دقیقه</option><option value="30">۳۰ دقیقه</option><option value="60">۱ ساعت</option><option value="240">۴ ساعت</option></select></label><button className="secondary-button" onClick={() => void load() } disabled={!token || loading}>تازه‌سازی</button><button className="primary-button" disabled title="ثبت استثنا به‌عنوان اقدام عملیاتی در API فعلی پشتیبانی نمی‌شود.">ثبت استثنا</button></div></header>
    <section className="sla-kpis" aria-label="خلاصهٔ هشدارهای همین صفحه"><article><small>هشدارهای این صفحه</small><strong>{token ? fa.format(items.length) : '—'}</strong><span>حداکثر ۵۰ ردیف از API</span></article><article><small>از موعد گذشته</small><strong className="sla-red">{token ? fa.format(overdueCount) : '—'}</strong><span>در snapshot همین صفحه</span></article><article><small>در افق پایش</small><strong>{token ? fa.format(dueSoon) : '—'}</strong><span>{horizon === '0' ? 'موعد فعلی' : `${fa.format(Number(horizon))} دقیقهٔ آینده`}</span></article><article><small>بدون پیک پذیرفته‌شده</small><strong>{token ? fa.format(unassignedCount) : '—'}</strong><span>نیازمند بررسی وضعیت</span></article><article><small>مبنای ارزیابی</small><strong className="sla-time">{evaluatedAt ? fmt(evaluatedAt) : '—'}</strong><span>زمان محاسبهٔ هشدار در API</span></article></section>
    {error && <div className="sla-error" role="alert">{error}</div>}
    <div className="sla-layout">
      <aside className="sla-list panel-card"><header><h2>صف رسیدگی</h2><span>{token ? fa.format(items.length) : '—'} هشدار</span></header><input aria-label="جست‌وجوی مأموریت" placeholder="جست‌وجوی شناسهٔ مأموریت یا مشتری" value={search} onChange={(event) => setSearch(event.target.value)} />
        <div className="sla-list-items">{visibleItems.map((item) => <button key={item.mission_id} className={`sla-alert-row ${selectedId === item.mission_id ? 'selected' : ''} ${item.is_overdue ? 'overdue' : ''}`} onClick={() => setSelectedId(item.mission_id)}><span className={`sla-badge ${item.is_overdue ? 'red' : 'amber'}`}>{item.is_overdue ? 'بحرانی' : 'در حال پایش'}</span><strong dir="ltr">{item.mission_id}</strong><small>{item.is_overdue ? 'عبور از موعد ثبت‌شده' : 'نزدیک‌شدن به موعد تحویل'}</small><small>موعد: {fmt(item.committed_delivery_at)}</small></button>)}{!visibleItems.length && <p className="sla-empty">{items.length ? 'هشداری با این جست‌وجو پیدا نشد.' : loading ? 'در حال دریافت هشدارها…' : token ? 'هشداری در این افق پایش از API برنگشت.' : 'برای شروع به API عملیات متصل شوید.'}</p>}</div>
        <footer><button className="secondary-button" disabled={!token || loading || cursorStack.length <= 1} onClick={() => { const stack = cursorStack.slice(0, -1); void load(stack[stack.length - 1] || null, stack) }}>قبلی</button><span>صفحهٔ {fa.format(cursorStack.length)}</span><button className="secondary-button" disabled={!token || loading || !cursor} onClick={() => { const stack = [...cursorStack, cursor]; void load(cursor, stack) }}>بعدی</button></footer>
      </aside>
      <main className="sla-detail">
        <section className="sla-selected panel-card"><header><span className={`sla-badge ${selected?.is_overdue ? 'red' : 'amber'}`}>{selected ? selected.is_overdue ? 'بحرانی' : 'پایش' : 'بدون انتخاب'}</span><div><h2>{selected ? `پایش پس از موعد — ${selected.mission_id}` : 'مأموریت دارای هشدار را انتخاب کنید'}</h2><p>{selected ? selected.is_overdue ? 'این مأموریت از موعد تحویل عبور کرده است؛ API زمان موعد و وضعیت فعلی را گزارش می‌کند.' : 'این مأموریت در افق زمانی انتخاب‌شده به موعد تحویل نزدیک است.' : 'جزئیات واقعی هشدار پس از دریافت داده از API نمایش داده می‌شود.'}</p></div></header><div className="sla-facts"><div><small>شناسهٔ مأموریت</small><strong dir="ltr">{selected?.mission_id || '—'}</strong></div><div><small>زمان ارزیابی</small><strong>{evaluatedAt ? fmt(evaluatedAt) : '—'}</strong></div><div><small>موعد تحویل</small><strong>{selected ? fmt(selected.committed_delivery_at) : '—'}</strong></div><div><small>وضعیت پیک</small><strong>{selected ? selected.courier_id ? 'پیک پذیرفته‌شده دارد' : 'پیک پذیرفته‌شده ندارد' : '—'}</strong></div></div></section>
        <div className="sla-detail-grid"><section className="panel-card sla-context"><header><h3>زمینهٔ سفارش</h3></header>{detailLoading ? <p>در حال دریافت جزئیات…</p> : detail ? <dl><div><dt>مشتری</dt><dd dir="ltr">{detail.customerId}</dd></div><div><dt>وضعیت مأموریت</dt><dd>{humanStatus[detail.status] || detail.status}</dd></div><div><dt>ایجاد مأموریت</dt><dd>{fmt(detail.createdAt)}</dd></div><div><dt>موعد قطعی</dt><dd>{fmt(detail.committedDeliveryAt)}</dd></div><div><dt>پیک پذیرفته‌شده</dt><dd dir="ltr">{selected?.courier_id || 'ندارد'}</dd></div><div><dt>زمان واقعی تحویل</dt><dd>از این API دریافت نمی‌شود</dd></div></dl> : <p>{selected ? 'جزئیات مأموریت از API دریافت نشد.' : 'با انتخاب یک هشدار، جزئیات سفارش نمایش داده می‌شود.'}</p>}</section>
          <section className="panel-card sla-cause"><header><h3>علت و شواهد</h3></header>{selected ? <dl><div><dt>نوع هشدار</dt><dd className={selected.is_overdue ? 'sla-red' : ''}>{selected.is_overdue ? 'عبور از موعد تحویل' : 'نزدیک‌شدن به موعد'}</dd></div><div><dt>وضعیت فعلی</dt><dd>{humanStatus[selected.status] || selected.status}</dd></div><div><dt>پیک</dt><dd dir="ltr">{selected.courier_id || 'تخصیص پذیرفته‌شده ندارد'}</dd></div><div><dt>موعد تعهدشده</dt><dd>{fmt(selected.committed_delivery_at)}</dd></div><div><dt>زمان واقعی تحویل</dt><dd>داده‌ای ارائه نشده</dd></div></dl> : <p>داده‌ای برای شواهد هشدار انتخاب نشده است.</p>}{detailError && <p className="sla-inline-error" role="alert">{detailError}</p>}</section>
          <section className="panel-card sla-timeline"><header><h3>Timeline عملیات</h3><span>{timelineSorted ? fa.format(timelineSorted.length) : '—'} رویداد</span></header>{detailLoading ? <p>در حال دریافت سابقه…</p> : timelineSorted?.length ? <ol>{timelineSorted.map((event, index) => <li key={`${event.eventId}-${index}`}><time>{fmt(event.occurredAt)}</time><span className="sla-dot" /><strong>{eventLabel[event.eventType] || event.eventType}</strong></li>)}</ol> : <p>{selected ? 'رویدادی در timeline برگشت داده نشد.' : 'پس از انتخاب هشدار، timeline مأموریت نمایش داده می‌شود.'}</p>}</section>
          <section className="panel-card sla-actions"><header><h3>اقدام عملیاتی</h3><span className="sla-badge neutral">خواندنی</span></header><p>امکان بازکردن جزئیات مأموریت فعال است. تغییر تخصیص یا ثبت تصمیم از API فعلی این پنل پشتیبانی نمی‌شود.</p><button className="primary-button" onClick={() => selected && onOpenMission(selected.mission_id)} disabled={!selected}>بازکردن جزئیات مأموریت</button><button className="secondary-button" disabled title="API بازتخصیص مأموریت در دسترس نیست.">تأیید تخصیص مجدد</button><button className="secondary-button" disabled title="API فهرست پیشنهادهای جایگزین این پنل را ارائه نمی‌کند.">مشاهدهٔ گزینه‌ها</button><button className="secondary-button" disabled title="ثبت رویداد SLA در API عملیات پشتیبانی نمی‌شود.">ثبت رخداد</button><button className="secondary-button" disabled title="بازگشایی مأموریت از این پنل پشتیبانی نمی‌شود.">بازکردن مأموریت</button></section>
        </div>
        <p className="sla-data-note">این هشدارها فقط snapshot ارزیابی API هستند. زمان ETA، علت ریشه‌ای، پاسخ پیک یا تغییر تخصیص از این فید قابل استنتاج نیست؛ آمار بالا نیز به همین صفحه و افق انتخاب‌شده محدود است.</p>
      </main>
    </div>
  </div>
}

type SlaExceptionsWorkbenchMission = { missionId: string; customerId: string; status: string; committedDeliveryAt: string; createdAt: string }
