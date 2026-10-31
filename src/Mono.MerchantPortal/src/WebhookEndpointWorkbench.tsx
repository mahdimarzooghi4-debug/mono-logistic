import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import './webhook-endpoint-workbench.css'

type Endpoint = { endpoint_id: string; url: string; events: string[]; status: string; created_at: string }
type Action = { endpoint: Endpoint; kind: 'disable' | 'rotate' }
type SecretReveal = { heading: string; endpointId: string; secret: string }

const events = [
  ['mission.accepted', 'پذیرش مأموریت'], ['mission.rejected', 'رد مأموریت'], ['promise.committed', 'ثبت تعهد تحویل'],
  ['courier.assigned', 'تخصیص پیک'], ['courier.arrived_pickup', 'رسیدن پیک به مبدا'], ['mission.handed_to_courier', 'تحویل به پیک'],
  ['mission.picked_up', 'دریافت مرسوله'], ['mission.in_transit', 'در مسیر'], ['mission.sla_at_risk', 'در معرض نقض SLA'],
  ['mission.delivered', 'تحویل نهایی'], ['mission.failed', 'ناموفق'], ['mission.cancelled', 'لغو مأموریت'],
  ['mission.return_required', 'نیازمند بازگشت'], ['mission.return_completed', 'تکمیل بازگشت'],
] as const
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'medium', timeStyle: 'short' })
function formatDate(value: string) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date) }

export default function WebhookEndpointWorkbench({ token, onBack }: { token: string; onBack: () => void }) {
  const [endpoints, setEndpoints] = useState<Endpoint[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [url, setUrl] = useState('')
  const [selectedEvents, setSelectedEvents] = useState<string[]>([])
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [pendingAction, setPendingAction] = useState<Action | null>(null)
  const [secretReveal, setSecretReveal] = useState<SecretReveal | null>(null)
  const requestSequence = useRef(0)

  const load = useCallback(async (cursor: string | null = null, stack: Array<string | null> = [null]) => {
    const sequence = ++requestSequence.current
    setLoading(true); setError('')
    try {
      const query = new URLSearchParams({ limit: '50' })
      if (cursor) query.set('cursor', cursor)
      const response = await fetch(`/v1/webhook-endpoints?${query}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      const result = await response.json() as { items?: Endpoint[]; next_cursor?: string | null }
      if (!Array.isArray(result.items)) throw new Error('پاسخ API فهرست Endpoint با قرارداد سازگار نیست.')
      if (requestSequence.current === sequence) { setEndpoints(result.items); setNextCursor(result.next_cursor || null); setCursorStack(stack) }
    } catch (cause) { if (requestSequence.current === sequence) { setEndpoints([]); setNextCursor(null); setError(cause instanceof Error ? cause.message : 'دریافت Endpointها ناموفق بود.') } }
    finally { if (requestSequence.current === sequence) setLoading(false) }
  }, [token])

  useEffect(() => { void load() }, [load])
  useEffect(() => () => { requestSequence.current += 1 }, [])

  async function readError(response: Response) {
    try {
      const body = await response.json() as { error?: { code?: string; message?: string }; title?: string; detail?: string }
      if (response.status === 403) return 'این کلید مشتری مجوز webhooks.manage ندارد. از مدیر Mono بخواهید کلید با این scope صادر کند.'
      return body.error?.message || body.error?.code || body.detail || body.title || `خطای سرور (${response.status})`
    } catch { return response.status === 403 ? 'این کلید مشتری مجوز webhooks.manage ندارد.' : `خطای سرور (${response.status})` }
  }

  async function createEndpoint(event: FormEvent) {
    event.preventDefault()
    if (selectedEvents.length === 0) { setError('حداقل یک رویداد انتخاب کنید.'); return }
    setBusy(true); setError(''); setNotice('')
    try {
      const response = await fetch('/v1/webhook-endpoints', { method: 'POST', headers: { Authorization: `Bearer ${token}`, Accept: 'application/json', 'Content-Type': 'application/json' }, body: JSON.stringify({ url: url.trim(), events: selectedEvents }), cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      const result = await response.json() as { endpoint_id: string; signing_secret: string }
      if (!result.endpoint_id || !result.signing_secret) throw new Error('ثبت انجام شد اما secret یک‌باره در پاسخ نبود. فهرست را تازه کنید و با پشتیبانی تماس بگیرید؛ secret را دوباره صادر نکنید.')
      setSecretReveal({ heading: 'Endpoint ثبت شد', endpointId: result.endpoint_id, secret: result.signing_secret })
      setUrl(''); setSelectedEvents([]); await load()
    } catch (cause) {
      setError(cause instanceof Error ? `${cause.message} اگر پاسخ شبکه نامشخص بود، ابتدا فهرست را تازه کنید؛ ثبت را کورکورانه تکرار نکنید.` : 'ثبت Endpoint ناموفق بود؛ ابتدا فهرست را تازه کنید.')
    } finally { setBusy(false) }
  }

  async function confirmAction() {
    if (!pendingAction) return
    const action = pendingAction
    setBusy(true); setError(''); setNotice('')
    try {
      const path = `/v1/webhook-endpoints/${encodeURIComponent(action.endpoint.endpoint_id)}/${action.kind === 'disable' ? 'disable' : 'rotate-secret'}`
      const response = await fetch(path, { method: 'POST', headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(await readError(response))
      if (action.kind === 'rotate') {
        const result = await response.json() as { endpoint_id: string; signing_secret: string }
        if (!result.signing_secret) throw new Error('چرخش انجام شد اما secret یک‌باره در پاسخ نبود. آن را دوباره تکرار نکنید؛ فهرست را تازه کنید و با پشتیبانی تماس بگیرید.')
        setSecretReveal({ heading: 'Secret جدید صادر شد', endpointId: action.endpoint.endpoint_id, secret: result.signing_secret })
      } else setNotice('Endpoint غیرفعال شد. ارسال‌های جدید به این مقصد انجام نمی‌شوند.')
      setPendingAction(null); await load()
    } catch (cause) { setError(cause instanceof Error ? `${cause.message} اگر پاسخ شبکه نامشخص بود، قبل از تکرار وضعیت را بررسی کنید.` : 'عملیات ناموفق بود؛ قبل از تکرار وضعیت را بررسی کنید.') }
    finally { setBusy(false) }
  }

  return <section className="panel settings-content webhook-endpoint-workbench">
    <div className="panel-heading"><div><span className="eyebrow">یکپارچه‌سازی</span><h2>مدیریت Webhook Endpoint</h2></div><button className="text-button" onClick={onBack}>بازگشت</button></div>
    <p className="data-note">درخواست‌ها با کلید مشتری و scope ‏<code dir="ltr">webhooks.manage</code> اجرا می‌شوند. URL فقط HTTPS پذیرفته می‌شود و سیاست مقصد در API اعتبارسنجی می‌شود. Secret فقط هنگام ثبت یا چرخش نمایش داده می‌شود.</p>
    {error && <div className="message error" role="alert"><strong>عملیات انجام نشد</strong><span>{error}</span></div>}
    {notice && <div className="message success" role="status">{notice}</div>}
    <form className="settings-form-grid" onSubmit={(event) => void createEndpoint(event)}>
      <label className="wide">نشانی HTTPS Endpoint<input type="url" required maxLength={2048} value={url} onChange={(event) => setUrl(event.target.value)} placeholder="https://api.example.com/mono/events" dir="ltr" /></label>
      <fieldset className="wide webhook-event-picker"><legend>رویدادهای ارسالی</legend><div>{events.map(([value, label]) => <label key={value}><input type="checkbox" checked={selectedEvents.includes(value)} onChange={(event) => setSelectedEvents((current) => event.target.checked ? [...current, value] : current.filter((item) => item !== value))} /><span>{label}</span><code dir="ltr">{value}</code></label>)}</div></fieldset>
      <div className="form-actions wide"><button className="primary-button" disabled={busy || loading}>{busy ? 'در حال ثبت…' : 'ثبت Endpoint'}</button><button type="button" className="secondary-button" onClick={() => void load()} disabled={busy || loading}>تازه‌سازی فهرست</button></div>
    </form>
    <div className="panel-heading webhook-endpoints-heading"><div><span className="eyebrow">حساب متصل</span><h3>Endpointهای ثبت‌شده</h3></div><span className="panel-meta">{loading ? 'در حال دریافت…' : `${new Intl.NumberFormat('fa-IR').format(endpoints.length)} در این صفحه`}</span></div>
    {!loading && !error && endpoints.length === 0 && <div className="empty-state"><span>⌁</span><strong>Endpoint ثبت‌شده‌ای وجود ندارد</strong><small>پس از ثبت، API امضای رویدادها را با secret صادرشده انجام می‌دهد.</small></div>}
    <div className="webhook-endpoint-list">{endpoints.map((endpoint) => <article className="webhook-endpoint-card" key={endpoint.endpoint_id}>
      <header><strong dir="ltr">{endpoint.endpoint_id}</strong><span className={`badge ${endpoint.status === 'ACTIVE' ? 'success' : 'neutral'}`}>{endpoint.status === 'ACTIVE' ? 'فعال' : 'غیرفعال'}</span></header>
      <p dir="ltr">{endpoint.url}</p><small>ثبت: {formatDate(endpoint.created_at)}</small><div className="webhook-endpoint-events">{endpoint.events.map((item) => <code dir="ltr" key={item}>{item}</code>)}</div>
      <div className="webhook-endpoint-actions">{endpoint.status === 'ACTIVE' && <><button className="secondary-button" disabled={busy} onClick={() => setPendingAction({ endpoint, kind: 'rotate' })}>چرخش secret</button><button className="secondary-button danger-action" disabled={busy} onClick={() => setPendingAction({ endpoint, kind: 'disable' })}>غیرفعال‌سازی</button></>}</div>
    </article>)}</div>
    <footer className="table-footer"><span>صفحهٔ {new Intl.NumberFormat('fa-IR').format(cursorStack.length)}</span><div className="pagination"><button disabled={loading || cursorStack.length <= 1} onClick={() => void load(cursorStack.at(-2) || null, cursorStack.slice(0, -1))}>قبلی</button><button disabled={loading || !nextCursor} onClick={() => void load(nextCursor, [...cursorStack, nextCursor])}>بعدی</button></div></footer>
    {pendingAction && <div className="modal-backdrop" role="presentation"><section className="timeline-modal" role="dialog" aria-modal="true" aria-labelledby="webhook-endpoint-action-title"><header><div><span className="eyebrow">تأیید تغییر تنظیمات</span><h2 id="webhook-endpoint-action-title">{pendingAction.kind === 'disable' ? 'غیرفعال‌کردن Endpoint' : 'چرخش signing secret'}</h2></div><button className="icon-button" disabled={busy} onClick={() => setPendingAction(null)} aria-label="بستن">×</button></header><p>{pendingAction.kind === 'disable' ? 'Endpoint غیرفعال می‌شود و دیگر مقصد ارسال رویدادهای جدید نخواهد بود.' : 'Secret جدید فقط یک بار نمایش داده می‌شود. پس از تأیید، secret قبلی باطل و secret جدید باید در سامانهٔ مقصد جایگزین شود.'}</p><div className="form-actions"><button className="primary-button" disabled={busy} onClick={() => void confirmAction()}>{busy ? 'در حال انجام…' : 'تأیید'}</button><button className="secondary-button" disabled={busy} onClick={() => setPendingAction(null)}>انصراف</button></div></section></div>}
    {secretReveal && <div className="modal-backdrop"><section className="timeline-modal secret-reveal-modal" role="dialog" aria-modal="true" aria-labelledby="webhook-secret-title"><header><div><span className="eyebrow">فقط همین بار قابل مشاهده است</span><h2 id="webhook-secret-title">{secretReveal.heading}</h2></div></header><p>این secret را اکنون در سامانهٔ مقصد ذخیره کنید. پس از بستن پنجره، Mono آن را دوباره نمایش نمی‌دهد.</p><p>شناسهٔ Endpoint: <bdi dir="ltr">{secretReveal.endpointId}</bdi></p><code className="webhook-one-time-secret" dir="ltr">{secretReveal.secret}</code><div className="form-actions"><button className="primary-button" onClick={() => setSecretReveal(null)}>ذخیره کردم و بستن</button></div></section></div>}
  </section>
}
