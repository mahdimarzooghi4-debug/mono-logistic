import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import './api-key-workbench.css'

type ApiKey = { keyId: string; customerId: string; scopes: string[]; createdAt: string; expiresAt: string; revokedAt: string | null }
type Audit = { keyId: string; customerId: string; action: string; actorId: string; reason: string; occurredAt: string }

const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
function formatDate(value: string | null | undefined) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date)
}

export default function ApiKeyWorkbench({ token }: { token: string }) {
  const [customerIdDraft, setCustomerIdDraft] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [keys, setKeys] = useState<ApiKey[]>([])
  const [auditByKey, setAuditByKey] = useState<Record<string, Audit[]>>({})
  const [expandedKey, setExpandedKey] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [auditLoading, setAuditLoading] = useState('')
  const [error, setError] = useState('')
  const [auditError, setAuditError] = useState('')
  const requestId = useRef(0)

  const load = useCallback(async (id: string) => {
    if (!token || !id.trim()) return
    const currentRequest = ++requestId.current
    setLoading(true); setError(''); setAuditError(''); setKeys([]); setAuditByKey({}); setExpandedKey(null)
    try {
      const response = await fetch(`/operations/v1/customers/${encodeURIComponent(id.trim())}/api-keys`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(response.status === 404 ? 'مشتری پیدا نشد.' : response.status === 401 || response.status === 403 ? 'دسترسی مدیر عملیات تأیید نشد.' : `دریافت کلیدها ناموفق بود (${response.status}).`)
      const result = await response.json() as { apiKeys?: ApiKey[] }
      if (!Array.isArray(result.apiKeys)) throw new Error('ساختار پاسخ API key با قرارداد API سازگار نیست.')
      if (currentRequest === requestId.current) { setCustomerId(id.trim()); setKeys(result.apiKeys) }
    } catch (cause) { if (currentRequest === requestId.current) setError(cause instanceof Error ? cause.message : 'دریافت کلیدها ناموفق بود.') }
    finally { if (currentRequest === requestId.current) setLoading(false) }
  }, [token])

  useEffect(() => { requestId.current += 1; setCustomerId(''); setKeys([]); setAuditByKey({}); setExpandedKey(null); setError('') }, [token])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!customerIdDraft.trim()) { setError('شناسهٔ مشتری را وارد کنید.'); return }
    await load(customerIdDraft)
  }

  async function toggleAudit(key: ApiKey) {
    if (expandedKey === key.keyId) { setExpandedKey(null); return }
    setExpandedKey(key.keyId); setAuditError('')
    if (auditByKey[key.keyId]) return
    setAuditLoading(key.keyId)
    try {
      const response = await fetch(`/operations/v1/customers/${encodeURIComponent(customerId)}/api-keys/${encodeURIComponent(key.keyId)}/audit`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' }, cache: 'no-store' })
      if (!response.ok) throw new Error(response.status === 404 ? 'کلید یا سابقهٔ آن پیدا نشد.' : `دریافت سابقه ناموفق بود (${response.status}).`)
      const result = await response.json() as { entries?: Audit[] }
      if (!Array.isArray(result.entries)) throw new Error('ساختار پاسخ سابقه با قرارداد API سازگار نیست.')
      setAuditByKey((current) => ({ ...current, [key.keyId]: result.entries! }))
    } catch (cause) { setAuditError(cause instanceof Error ? cause.message : 'دریافت سابقه ناموفق بود.') }
    finally { setAuditLoading('') }
  }

  return <section className="queue-panel api-key-workbench">
    <div className="panel-heading"><div><span className="eyebrow">دسترسی مشتریان</span><h2>فرادادهٔ کلیدهای API</h2></div><span className="panel-meta">نمایش کلید محرمانه غیرفعال است</span></div>
    <p className="api-key-note">این صفحه فقط فهرست و سوابق کلیدهای یک مشتری را از API مدیریتی می‌خواند؛ ساخت یا لغو کلید از این صفحه انجام نمی‌شود.</p>
    <form className="api-key-search" onSubmit={(event) => void submit(event)}>
      <label>شناسهٔ مشتری<input value={customerIdDraft} onChange={(event) => setCustomerIdDraft(event.target.value)} dir="ltr" autoComplete="off" maxLength={128} placeholder="cus_…" /></label>
      <button className="secondary-button" disabled={!token || loading}>{loading ? 'در حال دریافت…' : 'نمایش کلیدها'}</button>
      {customerId && <button type="button" className="text-button" disabled={loading} onClick={() => void load(customerId)}>تازه‌سازی</button>}
    </form>
    {error && <div className="webhook-inline-error" role="alert">{error}</div>}
    {!customerId && !loading && !error && <p className="onboarding-empty compact">برای شروع، شناسهٔ مشتری را وارد کنید.</p>}
    {customerId && !loading && !error && keys.length === 0 && <p className="onboarding-empty compact">برای این مشتری کلیدی ثبت نشده است.</p>}
    {keys.length > 0 && <div className="api-key-list">{keys.map((key) => {
      const expired = new Date(key.expiresAt).valueOf() <= Date.now()
      const status = key.revokedAt ? 'لغوشده' : expired ? 'منقضی‌شده' : 'فعال'
      return <article className="api-key-card" key={key.keyId}>
        <header><strong dir="ltr">{key.keyId}</strong><span className={`api-key-state ${key.revokedAt || expired ? 'inactive' : ''}`}>{status}</span></header>
        <dl className="worker-details"><div><dt>Scopes</dt><dd dir="ltr">{key.scopes.join(', ') || '—'}</dd></div><div><dt>ایجاد</dt><dd>{formatDate(key.createdAt)}</dd></div><div><dt>انقضا</dt><dd>{formatDate(key.expiresAt)}</dd></div>{key.revokedAt && <div><dt>لغو</dt><dd>{formatDate(key.revokedAt)}</dd></div>}</dl>
        <button type="button" className="history-button" onClick={() => void toggleAudit(key)}>{expandedKey === key.keyId ? 'بستن سابقه' : 'مشاهدهٔ سابقه'}</button>
        {expandedKey === key.keyId && <div className="api-key-audit">{auditLoading === key.keyId ? <p>در حال دریافت سابقه…</p> : auditError ? <p role="alert">{auditError}</p> : (auditByKey[key.keyId] || []).length ? auditByKey[key.keyId].map((entry, index) => <article key={`${entry.occurredAt}-${index}`}><strong>{entry.action}</strong><span>{formatDate(entry.occurredAt)}</span><small>{entry.actorId} · {entry.reason}</small></article>) : <p>برای این کلید سابقه‌ای برنگشت.</p>}</div>}
      </article>
    })}</div>}
  </section>
}
