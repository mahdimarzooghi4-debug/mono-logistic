import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import './policy-workbench.css'

type PolicySummary = {
  policyId: string
  policyType: string
  scope: string
  scopeId: string | null
  createdAt: string
  createdBy: string
  latestVersion: number
  latestStatus: string
}
type PolicyVersion = {
  policyId: string
  versionNumber: number
  enforcementType: string
  priority: number
  status: string
  effectiveFrom: string | null
  effectiveTo: string | null
  definition: unknown
  createdAt: string
  createdBy: string
}
type PolicyDetail = Pick<PolicySummary, 'policyId' | 'policyType' | 'scope' | 'scopeId' | 'createdAt' | 'createdBy'> & { versions: PolicyVersion[] }
type AuditEntry = { auditId: string; versionNumber: number; action: string; actorId: string; reason: string; occurredAt: string }
type ResolvedPolicy = { policyId: string; policyType: string; scope: string; scopeId: string | null; versionNumber: number; enforcementType: string; priority: number; effectiveFrom: string | null; effectiveTo: string | null; definition: unknown; precedenceTier: number }
type Evaluation = { requestedAt: string; evaluatedHardPolicyCount: number; ignoredNonHardPolicyCount: number; passed: boolean; candidateExcluded: boolean; reasonCodes: string[]; policies: Array<{ policyId: string; versionNumber: number; passed: boolean; candidateExcluded: boolean; reasonCodes: string[] }> }
type ApiError = { error?: { code?: string; message?: string } }
type Props = { token: string; reloadKey: number; onConnected: (connected: boolean) => void }

const fa = new Intl.NumberFormat('fa-IR')
const dateTime = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
const defaultDefinition = '{\n  "constraints": [\n    { "fact": "distance_km", "operator": "LTE", "value": 5, "reasonCode": "MAX_DISTANCE" }\n  ]\n}'

async function errorMessage(response: Response) {
  try {
    const body = await response.json() as ApiError
    const code = body.error?.code
    const message = body.error?.message
    if (response.status === 403 || response.status === 401) return 'دسترسی مدیر Mono با MFA تأیید نشد. مجوز را از API دریافت کنید.'
    return [code, message].filter(Boolean).join(' — ') || `خطای سرور (${response.status})`
  } catch {
    return response.status === 401 || response.status === 403
      ? 'دسترسی مدیر Mono با MFA تأیید نشد. مجوز را از API دریافت کنید.'
      : `پاسخ نامعتبر از سرور (${response.status})`
  }
}

function formattedDate(value: string | null) {
  if (!value) return 'بدون محدودیت زمانی'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTime.format(date)
}

function toIso(value: string) {
  return value ? new Date(value).toISOString() : null
}

export default function PolicyWorkbench({ token, reloadKey, onConnected }: Props) {
  const previousToken = useRef(token)
  const listRequestId = useRef(0)
  const detailRequestId = useRef(0)
  const [policies, setPolicies] = useState<PolicySummary[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [cursorStack, setCursorStack] = useState<Array<string | null>>([null])
  const [selected, setSelected] = useState<PolicyDetail | null>(null)
  const [audit, setAudit] = useState<AuditEntry[]>([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [success, setSuccess] = useState('')
  const [showCreate, setShowCreate] = useState(false)
  const [createVersion, setCreateVersion] = useState(false)
  const [policyType, setPolicyType] = useState('COVERAGE_ZONE')
  const [scope, setScope] = useState('CUSTOMER')
  const [scopeId, setScopeId] = useState('')
  const [enforcementType, setEnforcementType] = useState('HARD')
  const [priority, setPriority] = useState('10')
  const [effectiveFrom, setEffectiveFrom] = useState('')
  const [effectiveTo, setEffectiveTo] = useState('')
  const [definition, setDefinition] = useState(defaultDefinition)
  const [reason, setReason] = useState('')
  const [lifecycleReason, setLifecycleReason] = useState('')
  const [pendingLifecycle, setPendingLifecycle] = useState<{ versionNumber: number; action: 'activate' | 'retire' } | null>(null)
  const [serviceId, setServiceId] = useState('urban-delivery')
  const [customerId, setCustomerId] = useState('')
  const [contractId, setContractId] = useState('')
  const [branchId, setBranchId] = useState('')
  const [zoneIds, setZoneIds] = useState('')
  const [missionId, setMissionId] = useState('')
  const [runtimeId, setRuntimeId] = useState('')
  const [resolved, setResolved] = useState<ResolvedPolicy[] | null>(null)
  const [facts, setFacts] = useState('{\n  "distance_km": 4.5,\n  "mode": "BIKE"\n}')
  const [evaluation, setEvaluation] = useState<Evaluation | null>(null)

  const request = useCallback(async <T,>(path: string, init: RequestInit = {}): Promise<T> => {
    const headers = new Headers(init.headers)
    headers.set('Authorization', `Bearer ${token}`)
    headers.set('Accept', 'application/json')
    if (init.body) headers.set('Content-Type', 'application/json')
    const response = await fetch(path, {
      ...init,
      headers,
      cache: 'no-store',
    })
    if (!response.ok) throw new Error(await errorMessage(response))
    if (response.status === 204) return undefined as T
    return await response.json() as T
  }, [token])

  const loadPolicies = useCallback(async (after: string | null = null, pageStack: Array<string | null> = [null]) => {
    const requestId = ++listRequestId.current
    if (!token) { setLoading(false); setPolicies([]); setSelected(null); setAudit([]); setNextCursor(null); onConnected(false); return }
    setLoading(true); setError(''); setSuccess('')
    try {
      const params = new URLSearchParams({ page_size: '50' })
      if (after) params.set('cursor', after)
      const page = await request<{ policies: PolicySummary[]; nextCursor: string | null }>(`/operations/v1/policies?${params}`)
      if (requestId !== listRequestId.current) return
      if (!Array.isArray(page.policies)) throw new Error('ساختار فهرست سیاست‌ها با قرارداد API سازگار نیست.')
      setPolicies(page.policies); setNextCursor(page.nextCursor || null); setCursorStack(pageStack); onConnected(true)
    } catch (cause) {
      if (requestId !== listRequestId.current) return
      setPolicies([]); setNextCursor(null); onConnected(false)
      setError(cause instanceof Error ? cause.message : 'دریافت سیاست‌ها ناموفق بود.')
    } finally { if (requestId === listRequestId.current) setLoading(false) }
  }, [onConnected, request, token])

  const openPolicy = useCallback(async (policyId: string) => {
    if (!token) return
    const requestId = ++detailRequestId.current
    setSelected(null); setAudit([]); setDetailLoading(true); setError(''); setSuccess(''); setLifecycleReason(''); setPendingLifecycle(null)
    const encoded = encodeURIComponent(policyId)
    try {
      const [detail, history] = await Promise.all([
        request<PolicyDetail>(`/operations/v1/policies/${encoded}`),
        request<{ entries: AuditEntry[] }>(`/operations/v1/policies/${encoded}/audit`),
      ])
      if (requestId !== detailRequestId.current) return
      if (!Array.isArray(detail.versions) || !Array.isArray(history.entries)) throw new Error('ساختار جزئیات یا سابقهٔ سیاست معتبر نیست.')
      setSelected(detail); setAudit(history.entries)
    } catch (cause) {
      if (requestId !== detailRequestId.current) return
      setError(cause instanceof Error ? cause.message : 'جزئیات سیاست دریافت نشد.')
    } finally { if (requestId === detailRequestId.current) setDetailLoading(false) }
  }, [request, token])

  useEffect(() => { void loadPolicies(null) }, [loadPolicies, reloadKey])
  useEffect(() => {
    if (previousToken.current === token) return
    previousToken.current = token
    listRequestId.current += 1; detailRequestId.current += 1
    setDetailLoading(false)
    setSelected(null); setAudit([]); setResolved(null); setEvaluation(null); setShowCreate(false); setCreateVersion(false)
    setReason(''); setLifecycleReason(''); setPendingLifecycle(null); setDefinition(defaultDefinition)
    setCustomerId(''); setContractId(''); setBranchId(''); setZoneIds(''); setMissionId(''); setRuntimeId('')
    setFacts('{\n  "distance_km": 4.5,\n  "mode": "BIKE"\n}')
  }, [token])

  const context = useMemo(() => ({
    serviceId: serviceId.trim() || null,
    customerId: customerId.trim() || null,
    contractId: contractId.trim() || null,
    branchId: branchId.trim() || null,
    zoneIds: zoneIds.split(/[\s,،]+/).map((item) => item.trim()).filter(Boolean),
    missionId: missionId.trim() || null,
    runtimeId: runtimeId.trim() || null,
    requestedAt: null,
  }), [branchId, contractId, customerId, missionId, runtimeId, serviceId, zoneIds])

  function resetForm() {
    setPolicyType('COVERAGE_ZONE'); setScope('CUSTOMER'); setScopeId(''); setEnforcementType('HARD'); setPriority('10')
    setEffectiveFrom(''); setEffectiveTo(''); setDefinition(defaultDefinition); setReason('')
  }

  async function savePolicy(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(''); setSuccess('')
    if (!token || busy) return
    let parsedDefinition: unknown
    try { parsedDefinition = JSON.parse(definition) } catch { setError('تعریف سیاست باید JSON معتبر باشد.'); return }
    if (!parsedDefinition || typeof parsedDefinition !== 'object' || Array.isArray(parsedDefinition)) { setError('تعریف سیاست باید یک شیء JSON باشد.'); return }
    if (scope !== 'PLATFORM' && !scopeId.trim()) { setError('برای این دامنه، شناسهٔ دامنه الزامی است.'); return }
    setBusy(true)
    try {
      const body = {
        enforcementType, priority: Number(priority), effectiveFrom: toIso(effectiveFrom), effectiveTo: toIso(effectiveTo),
        definition: parsedDefinition, reason: reason.trim(),
      }
      const created = createVersion && selected
        ? await request<PolicyVersion>(`/operations/v1/policies/${encodeURIComponent(selected.policyId)}/versions`, { method: 'POST', body: JSON.stringify(body) })
        : await request<PolicyVersion>(`/operations/v1/policies`, { method: 'POST', body: JSON.stringify({ policyType, scope, scopeId: scope === 'PLATFORM' ? null : scopeId.trim(), ...body }) })
      setShowCreate(false); setCreateVersion(false); resetForm()
      await loadPolicies(null)
      if (created.policyId) await openPolicy(created.policyId)
      setSuccess('نسخهٔ پیش‌نویس از API ثبت شد؛ هنوز فعال نشده است.')
    } catch (cause) {
      setError(`${cause instanceof Error ? cause.message : 'ثبت پیش‌نویس ناموفق بود.'} اگر پاسخ شبکه نامشخص بود، ابتدا فهرست را تازه کنید و وجود نسخه را بررسی کنید؛ درخواست را کورکورانه تکرار نکنید.`)
    } finally { setBusy(false) }
  }

  const currentCursor = cursorStack[cursorStack.length - 1] || null

  async function lifecycle(version: PolicyVersion, action: 'activate' | 'retire') {
    if (!selected || !lifecycleReason.trim() || busy) return
    setBusy(true); setError(''); setSuccess('')
    try {
      await request<PolicyVersion>(`/operations/v1/policies/${encodeURIComponent(selected.policyId)}/versions/${version.versionNumber}/${action}`, {
        method: 'POST', body: JSON.stringify({ reason: lifecycleReason.trim() }),
      })
      await openPolicy(selected.policyId); await loadPolicies(currentCursor, cursorStack)
      setLifecycleReason(''); setPendingLifecycle(null)
      setSuccess(action === 'activate' ? 'نسخه فعال شد. این تغییر به‌تنهایی روی درخواست یا مأموریت اعمال نمی‌شود.' : 'نسخه بازنشسته شد؛ تعریف و سابقه حفظ شدند.')
    } catch (cause) {
      const failure = cause instanceof Error ? cause.message : 'عملیات ناموفق بود.'
      setError(`${failure} وضعیت نسخه را دوباره از API بخوانید؛ مجوز و انتقال وضعیت در سرور کنترل می‌شود.`)
      await openPolicy(selected.policyId)
      setPendingLifecycle(null)
    } finally { setBusy(false) }
  }

  async function resolveEffective(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(''); setSuccess(''); setResolved(null)
    if (!token || busy) return
    const params = new URLSearchParams()
    if (context.serviceId) params.set('service_id', context.serviceId)
    if (context.customerId) params.set('customer_id', context.customerId)
    if (context.contractId) params.set('contract_id', context.contractId)
    if (context.branchId) params.set('branch_id', context.branchId)
    context.zoneIds.forEach((zone) => params.append('zone_id', zone))
    if (context.missionId) params.set('mission_id', context.missionId)
    if (context.runtimeId) params.set('runtime_id', context.runtimeId)
    setBusy(true)
    try {
      const result = await request<{ requestedAt: string; policies: ResolvedPolicy[] }>(`/operations/v1/policies/effective?${params}`)
      if (!Array.isArray(result.policies)) throw new Error('ساختار پاسخ پیش‌نمایش مؤثر معتبر نیست.')
      setResolved(result.policies); onConnected(true)
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'پیش‌نمایش سیاست مؤثر ناموفق بود.') }
    finally { setBusy(false) }
  }

  async function evaluateHard(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(''); setSuccess(''); setEvaluation(null)
    if (!token || busy) return
    let parsedFacts: unknown
    try { parsedFacts = JSON.parse(facts) } catch { setError('Facts باید JSON معتبر باشد.'); return }
    if (!parsedFacts || typeof parsedFacts !== 'object' || Array.isArray(parsedFacts)) { setError('Facts باید یک شیء JSON باشد.'); return }
    setBusy(true)
    try {
      const result = await request<Evaluation>('/operations/v1/policies/evaluate', {
        method: 'POST', body: JSON.stringify({ ...context, facts: parsedFacts }),
      })
      if (typeof result.passed !== 'boolean' || !Array.isArray(result.policies)) throw new Error('ساختار پاسخ ارزیابی HARD معتبر نیست.')
      setEvaluation(result); onConnected(true)
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'ارزیابی محدود ناموفق بود.') }
    finally { setBusy(false) }
  }

  const latest = selected?.versions.slice().sort((a, b) => b.versionNumber - a.versionNumber) || []

  return <div className="policy-workbench" dir="rtl">
    <div className="policy-safety-note"><strong>پیش‌نمایش عملیات</strong><span>فهرست، resolve و evaluate فقط رجیستری سیاست را می‌خوانند/ارزیابی می‌کنند؛ خروجی این صفحه به درخواست تحویل یا مأموریت اعمال نمی‌شود.</span></div>
    {error && <div className="policy-error" role="alert"><strong>خطا</strong><span>{error}</span></div>}
    {success && <div className="policy-success" role="status">{success}</div>}

    <section className="queue-panel policy-catalog">
      <div className="panel-heading"><div><span className="eyebrow">رجیستری مجاز Operations</span><h2>سیاست‌ها و نسخه‌ها</h2></div><div className="policy-toolbar"><button className="secondary-button" type="button" onClick={() => void loadPolicies(currentCursor, cursorStack)} disabled={!token || loading || busy}>تازه‌سازی</button><button className="primary-button" type="button" onClick={() => { resetForm(); setCreateVersion(false); setShowCreate((value) => !value) }} disabled={!token || busy}>سیاست جدید</button></div></div>
      {loading ? <div className="policy-state"><span className="spinner"/>در حال دریافت از API…</div> : policies.length === 0 ? <div className="policy-state">{token ? 'سیاستی در این صفحه از API برنگشت.' : 'برای دیدن دادهٔ واقعی، به API عملیات متصل شوید.'}</div> : <div className="table-wrap"><table className="policy-table"><thead><tr><th>شناسه</th><th>خانواده</th><th>دامنه</th><th>آخرین نسخه</th><th>وضعیت</th><th>جزئیات</th></tr></thead><tbody>{policies.map((item) => <tr key={item.policyId}><td dir="ltr">{item.policyId}</td><td>{item.policyType}</td><td>{item.scope}{item.scopeId ? <small dir="ltr">{item.scopeId}</small> : null}</td><td>{fa.format(item.latestVersion)}</td><td><span className={`badge ${item.latestStatus === 'ACTIVE' ? 'success' : item.latestStatus === 'DRAFT' ? 'warning' : 'neutral'}`}>{item.latestStatus}</span></td><td><button className="history-button" type="button" onClick={() => void openPolicy(item.policyId)}>بازکردن</button></td></tr>)}</tbody></table></div>}
      <footer className="table-footer"><span>{fa.format(policies.length)} سیاست در این صفحه</span><div className="policy-toolbar"><button className="history-button" type="button" onClick={() => { const previous = cursorStack.slice(0, -1); void loadPolicies(previous[previous.length - 1] || null, previous.length ? previous : [null]) }} disabled={!token || loading || cursorStack.length < 2}>قبلی</button><button className="history-button" type="button" onClick={() => nextCursor && void loadPolicies(nextCursor, [...cursorStack, nextCursor])} disabled={!token || loading || !nextCursor}>بعدی</button></div></footer>
    </section>

    {showCreate && <section className="queue-panel policy-form-panel">
      <div className="panel-heading"><div><span className="eyebrow">Draft version</span><h2>{createVersion ? `نسخهٔ جدید برای ${selected?.policyId}` : 'ایجاد سیاست و نسخهٔ اول'}</h2></div><button className="text-button" type="button" onClick={() => { setShowCreate(false); setCreateVersion(false) }}>بستن</button></div>
      <form className="policy-form" onSubmit={savePolicy}>
        {!createVersion && <label>خانوادهٔ سیاست<input value={policyType} onChange={(event) => setPolicyType(event.target.value)} maxLength={80} required dir="ltr" /></label>}
        {!createVersion && <label>دامنه<select value={scope} onChange={(event) => setScope(event.target.value)}><option>PLATFORM</option><option>SERVICE</option><option>CUSTOMER</option><option>BRANCH</option><option>ZONE</option><option>CONTRACT</option><option>MISSION</option><option>RUNTIME</option></select></label>}
        {!createVersion && scope !== 'PLATFORM' && <label>شناسهٔ دامنه<input value={scopeId} onChange={(event) => setScopeId(event.target.value)} maxLength={128} required dir="ltr" /></label>}
        <label>نوع اعمال<select value={enforcementType} onChange={(event) => setEnforcementType(event.target.value)}>{['HARD','OPTIMIZATION','COMMERCIAL','CONFIGURATION','CONTRACTUAL','WORKFLOW','FINANCIAL','RISK'].map((item) => <option key={item}>{item}</option>)}</select></label>
        <label>اولویت<input type="number" min="1" max="2147483647" value={priority} onChange={(event) => setPriority(event.target.value)} required /></label>
        <label>شروع اعتبار<input type="datetime-local" value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} /></label>
        <label>پایان اعتبار<input type="datetime-local" value={effectiveTo} onChange={(event) => setEffectiveTo(event.target.value)} /></label>
        <label className="policy-definition">تعریف JSON<textarea value={definition} onChange={(event) => setDefinition(event.target.value)} rows={8} dir="ltr" spellCheck={false} required /></label>
        <label className="policy-reason">دلیل ایجاد نسخه<input value={reason} onChange={(event) => setReason(event.target.value)} maxLength={1000} required /></label>
        <div className="policy-form-actions"><button className="primary-button" type="submit" disabled={!token || busy}>{busy ? 'در حال ثبت…' : 'ثبت نسخهٔ پیش‌نویس'}</button><span>ثبت Draft، سیاست را فعال نمی‌کند.</span></div>
      </form>
    </section>}

    {detailLoading && <section className="queue-panel policy-state"><span className="spinner"/>در حال دریافت نسخه‌ها و سابقه…</section>}
    {selected && !detailLoading && <section className="queue-panel policy-detail">
      <div className="panel-heading"><div><span className="eyebrow">جزئیات و تاریخچه</span><h2 dir="ltr">{selected.policyId}</h2></div><div className="policy-toolbar"><button className="secondary-button" type="button" onClick={() => void openPolicy(selected.policyId)} disabled={busy}>تازه‌سازی جزئیات</button><button className="primary-button" type="button" onClick={() => { resetForm(); setCreateVersion(true); setShowCreate(true) }} disabled={busy}>نسخهٔ Draft جدید</button></div></div>
      <dl className="policy-meta"><div><dt>خانواده</dt><dd>{selected.policyType}</dd></div><div><dt>دامنه</dt><dd>{selected.scope} {selected.scopeId || ''}</dd></div><div><dt>سازنده</dt><dd dir="ltr">{selected.createdBy}</dd></div><div><dt>زمان ایجاد</dt><dd>{formattedDate(selected.createdAt)}</dd></div></dl>
      <div className="policy-reason-field"><label htmlFor="policyLifecycleReason">دلیل فعال‌سازی یا بازنشست <span>(الزامی؛ مدیر Mono با MFA)</span></label><input id="policyLifecycleReason" value={lifecycleReason} onChange={(event) => setLifecycleReason(event.target.value)} maxLength={1000} placeholder="دلیل قابل ممیزی را وارد کنید" /></div>
      <div className="table-wrap"><table className="policy-table"><thead><tr><th>نسخه</th><th>نوع / اولویت</th><th>اعتبار</th><th>وضعیت</th><th>تعریف</th><th>اقدام</th></tr></thead><tbody>{latest.map((version) => <tr key={version.versionNumber}><td>{fa.format(version.versionNumber)}<small dir="ltr">{version.createdBy}</small></td><td>{version.enforcementType}<small>{fa.format(version.priority)}</small></td><td>{formattedDate(version.effectiveFrom)}<small>تا {formattedDate(version.effectiveTo)}</small></td><td><span className={`badge ${version.status === 'ACTIVE' ? 'success' : version.status === 'DRAFT' ? 'warning' : 'neutral'}`}>{version.status}</span></td><td><details><summary>نمایش JSON</summary><pre dir="ltr">{JSON.stringify(version.definition, null, 2)}</pre></details></td><td><div className="policy-row-actions">{version.status === 'DRAFT' && <button type="button" className="history-button" disabled={!token || busy || !lifecycleReason.trim()} onClick={() => setPendingLifecycle({ versionNumber: version.versionNumber, action: 'activate' })}>فعال‌سازی</button>}{(version.status === 'ACTIVE' || version.status === 'INACTIVE') && <button type="button" className="history-button danger-action" disabled={!token || busy || !lifecycleReason.trim()} onClick={() => setPendingLifecycle({ versionNumber: version.versionNumber, action: 'retire' })}>بازنشست</button>}{pendingLifecycle?.versionNumber === version.versionNumber && <div className="policy-confirm" role="group" aria-label="تأیید تغییر چرخهٔ سیاست"><span>{pendingLifecycle.action === 'retire' ? 'بازنشست دائمی نسخه را تأیید می‌کنید؟' : 'فعال‌سازی نسخه را تأیید می‌کنید؟ نسخهٔ فعال قبلی غیرفعال می‌شود.'}</span><button type="button" className={pendingLifecycle.action === 'retire' ? 'history-button danger-action' : 'history-button'} disabled={!token || busy || !lifecycleReason.trim()} onClick={() => void lifecycle(version, pendingLifecycle.action)}>تأیید</button><button type="button" className="history-button" onClick={() => setPendingLifecycle(null)} disabled={busy}>انصراف</button></div>}</div></td></tr>)}</tbody></table></div>
      <section className="policy-audit"><h3>سابقهٔ ممیزی سیاست</h3>{audit.length ? <div className="table-wrap"><table className="policy-table"><thead><tr><th>زمان</th><th>عمل</th><th>بازیگر</th><th>نسخه</th><th>دلیل</th></tr></thead><tbody>{audit.map((entry) => <tr key={entry.auditId}><td>{formattedDate(entry.occurredAt)}</td><td>{entry.action}</td><td dir="ltr">{entry.actorId}</td><td>{fa.format(entry.versionNumber)}</td><td>{entry.reason}</td></tr>)}</tbody></table></div> : <p>API سابقه‌ای برای این سیاست برنگرداند.</p>}</section>
    </section>}

    <section className="policy-lab">
      <section className="queue-panel policy-lab-card"><div className="panel-heading"><div><span className="eyebrow">Resolve</span><h2>پیش‌نمایش سیاست مؤثر</h2></div></div><p className="data-note">فقط نسخه‌های فعال و مؤثر در زمان درخواست، با ترتیب تقدم API نمایش داده می‌شوند.</p>
        <form className="policy-context-form" onSubmit={resolveEffective}><label>سرویس<input value={serviceId} onChange={(event) => setServiceId(event.target.value)} dir="ltr" /></label><label>مشتری<input value={customerId} onChange={(event) => setCustomerId(event.target.value)} dir="ltr" /></label><label>قرارداد<input value={contractId} onChange={(event) => setContractId(event.target.value)} dir="ltr" /></label><label>شعبه<input value={branchId} onChange={(event) => setBranchId(event.target.value)} dir="ltr" /></label><label>زون‌ها<input value={zoneIds} onChange={(event) => setZoneIds(event.target.value)} placeholder="شناسه‌ها با فاصله یا ویرگول" dir="ltr" /></label><label>مأموریت<input value={missionId} onChange={(event) => setMissionId(event.target.value)} dir="ltr" /></label><label>Runtime<input value={runtimeId} onChange={(event) => setRuntimeId(event.target.value)} dir="ltr" /></label><button className="secondary-button" type="submit" disabled={!token || busy}>resolve از API</button></form>
        {resolved && <div className="policy-result" role="status"><strong>{fa.format(resolved.length)} سیاست مؤثر</strong>{resolved.length ? resolved.map((item) => <article key={`${item.policyId}-${item.versionNumber}`}><span dir="ltr">{item.policyId} · v{item.versionNumber}</span><small>{item.scope} / {item.enforcementType} / تقدم {fa.format(item.precedenceTier)}</small></article>) : <p>سیاست فعالی برای این context از API برنگشت.</p>}</div>}
      </section>
      <section className="queue-panel policy-lab-card"><div className="panel-heading"><div><span className="eyebrow">HARD preview</span><h2>ارزیابی محدود facts</h2></div></div><p className="data-note">ارزیابی preview است؛ هیچ درخواست یا مأموریتی تغییر نمی‌کند.</p>
        <form className="policy-evaluate-form" onSubmit={evaluateHard}><label>Facts به صورت JSON<textarea value={facts} onChange={(event) => setFacts(event.target.value)} rows={7} dir="ltr" spellCheck={false} /></label><button className="secondary-button" type="submit" disabled={!token || busy}>ارزیابی از API</button></form>
        {evaluation && <div className={`policy-result ${evaluation.passed ? 'passed' : 'blocked'}`} role="status"><strong>{evaluation.passed ? 'ارزیابی موفق' : 'ارزیابی ناموفق'} · {fa.format(evaluation.evaluatedHardPolicyCount)} HARD · {fa.format(evaluation.ignoredNonHardPolicyCount)} غیر-HARD نادیده‌گرفته‌شده</strong><span>{evaluation.candidateExcluded ? 'نامزد در این پیش‌نمایش کنار گذاشته می‌شود.' : 'نامزد در این پیش‌نمایش کنار گذاشته نشد.'}</span>{evaluation.reasonCodes.map((code) => <small key={code} dir="ltr">{code}</small>)}</div>}
      </section>
    </section>
  </div>
}
