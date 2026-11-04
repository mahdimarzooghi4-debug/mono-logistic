import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import './onboarding-workbench.css'

type Customer = { customerId: string; legalName: string; displayName: string | null; status: string; customerType: string | null; defaultChannel: string | null; defaultCurrency: string | null; createdAt: string }
type CustomerPage = { customers: Customer[]; nextCursor: string | null }
type Branch = { branchId: string; customerId: string; name: string; address: string | null; latitude: number | null; longitude: number | null; zoneId: string | null; status: string; operatingHours?: unknown }
type Audit = { action: string; actorId: string; reason: string; occurredAt: string; auditId?: string }
type AuditPage = { entries: Audit[]; nextCursor: string | null }
type ApiKey = { keyId: string; customerId: string; scopes: string[]; createdAt: string; expiresAt: string; revokedAt: string | null }
type Props = { token: string; reloadKey: number; onConnected: (connected: boolean) => void }

const fa = new Intl.NumberFormat('fa-IR')
const dt = new Intl.DateTimeFormat('fa-IR', { dateStyle: 'short', timeStyle: 'short' })
const fmtDate = (value: string) => { const date = new Date(value); return Number.isNaN(date.valueOf()) ? '—' : dt.format(date) }

export default function OnboardingWorkbench({ token, reloadKey, onConnected }: Props) {
  const [customers, setCustomers] = useState<Customer[]>([])
  const [cursor, setCursor] = useState<string | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [pageStack, setPageStack] = useState<Array<string | null>>([null])
  const [searchDraft, setSearchDraft] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('PENDING')
  const [selected, setSelected] = useState<Customer | null>(null)
  const [customerAudit, setCustomerAudit] = useState<Audit[]>([])
  const [branches, setBranches] = useState<Branch[]>([])
  const [branchAudit, setBranchAudit] = useState<Audit[]>([])
  const [branchAuditNextCursor, setBranchAuditNextCursor] = useState<string | null>(null)
  const [apiKeys, setApiKeys] = useState<ApiKey[]>([])
  const [branchDetail, setBranchDetail] = useState<Branch | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [branchOpen, setBranchOpen] = useState(false)
  const [pendingActivation, setPendingActivation] = useState<Customer | null>(null)
  const [activationReason, setActivationReason] = useState('')
  const [createForm, setCreateForm] = useState({ legalName: '', displayName: '', customerType: 'MERCHANT', defaultChannel: 'B2B_API', defaultCurrency: 'IRT', reason: '' })
  const [branchForm, setBranchForm] = useState({ branchId: '', name: '', address: '', latitude: '', longitude: '', zoneId: '', operatingHours: '{}', reason: '' })
  const requestId = useRef(0)
  const detailRequestId = useRef(0)
  const tokenAtMount = useRef(token)

  const api = useCallback(async <T,>(path: string, init: RequestInit = {}): Promise<T> => {
    const headers = new Headers(init.headers)
    headers.set('Authorization', `Bearer ${token}`)
    headers.set('Accept', 'application/json')
    if (init.body) headers.set('Content-Type', 'application/json')
    const response = await fetch(path, { ...init, headers, cache: 'no-store' })
    if (!response.ok) {
      let detail = ''
      try { const body = await response.json(); detail = [body.error?.code, body.error?.message].filter(Boolean).join(' — ') } catch { /* use status below */ }
      if (response.status === 401 || response.status === 403) throw new Error('دسترسی مدیر Mono با MFA تأیید نشد؛ پاسخ مجوز را فقط API تعیین می‌کند.')
      throw new Error(detail || `خطای سرور (${response.status})`)
    }
    if (response.status === 204) return undefined as T
    return await response.json() as T
  }, [token])

  const loadCustomers = useCallback(async (after: string | null = null, stack: Array<string | null> = [null]) => {
    const id = ++requestId.current
    if (!token) { setCustomers([]); setSelected(null); setNextCursor(null); onConnected(false); return }
    setLoading(true); setError(''); setNotice('')
    try {
      const params = new URLSearchParams({ page_size: '50' })
      if (status) params.set('status', status)
      if (search) params.set('search', search)
      if (after) params.set('cursor', after)
      const page = await api<CustomerPage>(`/operations/v1/customers?${params}`)
      if (id !== requestId.current) return
      setCustomers(page.customers); setNextCursor(page.nextCursor); setPageStack(stack); setCursor(after); onConnected(true)
      setSelected((current) => current && !page.customers.some((item) => item.customerId === current.customerId) ? null : current)
    } catch (cause) {
      if (id === requestId.current) { setError(cause instanceof Error ? cause.message : 'دریافت فهرست مشتریان ناموفق بود.'); onConnected(false) }
    } finally { if (id === requestId.current) setLoading(false) }
  }, [api, onConnected, search, status, token])

  const loadDetail = useCallback(async (customer: Customer) => {
    const id = ++detailRequestId.current
    setSelected(customer); setCustomerAudit([]); setBranches([]); setApiKeys([]); setBranchAudit([]); setBranchDetail(null); setError('')
    try {
      const [audit, branchPage, keyPage] = await Promise.all([
        api<{ entries: Audit[] }>(`/operations/v1/customers/${encodeURIComponent(customer.customerId)}/audit`),
        api<{ branches: Branch[] }>(`/operations/v1/customers/${encodeURIComponent(customer.customerId)}/branches`),
        api<{ apiKeys: ApiKey[] }>(`/operations/v1/customers/${encodeURIComponent(customer.customerId)}/api-keys`),
      ])
      if (id !== detailRequestId.current) return
      setCustomerAudit(audit.entries); setBranches(branchPage.branches); setApiKeys(keyPage.apiKeys ?? [])
    } catch (cause) { if (id === detailRequestId.current) setError(cause instanceof Error ? cause.message : 'دریافت جزئیات مشتری ناموفق بود.') }
  }, [api])

  useEffect(() => { void loadCustomers() }, [loadCustomers, reloadKey])
  useEffect(() => {
    if (tokenAtMount.current === token) return
    tokenAtMount.current = token
    requestId.current += 1; detailRequestId.current += 1
    setCustomers([]); setSelected(null); setCustomerAudit([]); setBranches([]); setBranchDetail(null); setBranchAudit([]); setBranchAuditNextCursor(null); setNextCursor(null); setError('')
  }, [token])

  function applySearch(event: FormEvent) { event.preventDefault(); setSearch(searchDraft.trim()); void loadCustomers(null, [null]) }
  async function createCustomer(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('')
    try {
      const created = await api<Customer>('/operations/v1/customers', { method: 'POST', body: JSON.stringify({ ...createForm, displayName: createForm.displayName || null }) })
      setCreateOpen(false); setCreateForm({ legalName: '', displayName: '', customerType: 'MERCHANT', defaultChannel: 'B2B_API', defaultCurrency: 'IRT', reason: '' })
      setStatus('PENDING'); setSearch(''); setSearchDraft('')
      await loadCustomers(null, [null]); await loadDetail(created)
      setNotice('مشتری به‌صورت PENDING ثبت شد؛ ایجاد این رکورد به‌تنهایی API key یا دسترسی صادر نمی‌کند.')
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'ثبت مشتری ناموفق بود.') } finally { setBusy(false) }
  }
  async function activateCustomer() {
    if (!pendingActivation || activationReason.trim().length < 2) return
    setBusy(true); setError('')
    try {
      await api<void>(`/operations/v1/customers/${encodeURIComponent(pendingActivation.customerId)}/activate`, { method: 'POST', body: JSON.stringify({ reason: activationReason.trim() }) })
      const activated = { ...pendingActivation, status: 'ACTIVE' }
      setPendingActivation(null); setActivationReason('')
      await loadCustomers(cursor, pageStack); await loadDetail(activated)
      setNotice('فعال‌سازی توسط API ثبت شد؛ صدور credential همچنان فرایندی جداگانه است.')
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'فعال‌سازی مشتری ناموفق بود.') } finally { setBusy(false) }
  }
  async function saveBranch(event: FormEvent) {
    event.preventDefault(); if (!selected) return
    setBusy(true); setError('')
    try {
      let operatingHours: unknown
      try { operatingHours = JSON.parse(branchForm.operatingHours) } catch { throw new Error('ساعات کاری باید JSON معتبر باشد.') }
      if (operatingHours === null || Array.isArray(operatingHours) || typeof operatingHours !== 'object') throw new Error('ساعات کاری باید یک شیء JSON باشد.')
      const latitude = branchForm.latitude.trim() ? Number(branchForm.latitude) : null
      const longitude = branchForm.longitude.trim() ? Number(branchForm.longitude) : null
      if ((latitude === null) !== (longitude === null)) throw new Error('عرض و طول جغرافیایی باید با هم وارد شوند.')
      const body = { name: branchForm.name.trim(), address: branchForm.address || null, latitude, longitude, zoneId: branchForm.zoneId || null, operatingHours, reason: branchForm.reason.trim() }
      const path = `/operations/v1/customers/${encodeURIComponent(selected.customerId)}/branches${branchForm.branchId ? `/${encodeURIComponent(branchForm.branchId)}` : ''}`
      const saved = await api<Branch>(path, { method: branchForm.branchId ? 'PUT' : 'POST', body: JSON.stringify(body) })
      setBranchOpen(false); setBranchForm({ branchId: '', name: '', address: '', latitude: '', longitude: '', zoneId: '', operatingHours: '{}', reason: '' }); setNotice(branchForm.branchId ? 'تغییرات شعبه با سابقهٔ ممیزی ذخیره شد.' : 'شعبهٔ فعال در API ثبت شد.')
      await loadDetail(selected)
      const detail = await api<Branch>(`/operations/v1/customers/${encodeURIComponent(selected.customerId)}/branches/${encodeURIComponent(saved.branchId)}`)
      setBranchDetail(detail); await loadBranchAudit(saved.branchId)
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'ذخیرهٔ شعبه ناموفق بود.') } finally { setBusy(false) }
  }
  async function loadBranchAudit(branchId: string, after: string | null = null, append = false) {
    if (!selected) return
    const id = detailRequestId.current
    try {
      const params = new URLSearchParams({ page_size: '50' })
      if (after) params.set('cursor', after)
      const page = await api<AuditPage>(`/operations/v1/customers/${encodeURIComponent(selected.customerId)}/branches/${encodeURIComponent(branchId)}/audit?${params}`)
      if (id !== detailRequestId.current) return
      setBranchAudit((current) => append ? [...current, ...page.entries] : page.entries); setBranchAuditNextCursor(page.nextCursor)
    }
    catch (cause) { if (id === detailRequestId.current) setError(cause instanceof Error ? cause.message : 'دریافت سابقهٔ شعبه ناموفق بود.') }
  }
  async function inspectBranch(branchId: string): Promise<Branch | null> {
    if (!selected) return null
    const id = ++detailRequestId.current
    setError('')
    try {
      const detail = await api<Branch>(`/operations/v1/customers/${encodeURIComponent(selected.customerId)}/branches/${encodeURIComponent(branchId)}`)
      if (id !== detailRequestId.current) return null
      setBranchDetail(detail); setBranchAudit([]); setBranchAuditNextCursor(null); await loadBranchAudit(branchId); return detail
    } catch (cause) { if (id === detailRequestId.current) setError(cause instanceof Error ? cause.message : 'دریافت شعبه ناموفق بود.'); return null }
  }
  function editBranch(branch: Branch) {
    setBranchForm({ branchId: branch.branchId, name: branch.name, address: branch.address || '', latitude: branch.latitude?.toString() || '', longitude: branch.longitude?.toString() || '', zoneId: branch.zoneId || '', operatingHours: JSON.stringify(branch.operatingHours ?? {}, null, 2), reason: '' })
    setBranchOpen(true)
  }

  return <div className="onboarding-workbench">
    <div className="onboarding-toolbar"><form className="onboarding-filters" onSubmit={applySearch}><label>وضعیت مشتری<select value={status} onChange={(event) => setStatus(event.target.value)}><option value="">همه</option><option value="PENDING">در انتظار</option><option value="ACTIVE">فعال</option></select></label><label className="onboarding-search">جست‌وجوی پیشوند شناسه، نام حقوقی یا نمایشی<input value={searchDraft} onChange={(event) => setSearchDraft(event.target.value)} maxLength={128} /></label><button className="secondary-button" disabled={!token || loading}>جست‌وجو</button></form><button className="primary-button" onClick={() => setCreateOpen(true)} disabled={!token}>ثبت مشتری</button></div>
    {error && <div className="onboarding-error" role="alert">{error}</div>}{notice && <div className="onboarding-notice" role="status">{notice}</div>}
    {!token ? <div className="onboarding-empty">برای دسترسی به پرونده‌های راه‌اندازی، با توکن مدیر عملیات و MFA متصل شوید.</div> : <div className="onboarding-layout">
      <section className="onboarding-card customer-list"><header><div><span className="eyebrow">صف راه‌اندازی</span><h2>مشتریان <small>{fa.format(customers.length)} مورد در این صفحه</small></h2></div><button className="secondary-button" onClick={() => void loadCustomers(cursor, pageStack)} disabled={loading}>تازه‌سازی</button></header>
        {loading ? <p className="onboarding-empty">در حال دریافت از API…</p> : customers.length === 0 ? <p className="onboarding-empty">برای این فیلتر موردی در API برنگشت.</p> : <div className="onboarding-customer-rows">{customers.map((customer) => <button key={customer.customerId} className={`onboarding-customer-row ${selected?.customerId === customer.customerId ? 'selected' : ''}`} onClick={() => void loadDetail(customer)}><span><strong>{customer.displayName || customer.legalName}</strong><small>{customer.legalName}</small><code dir="ltr">{customer.customerId}</code></span><em className={customer.status === 'ACTIVE' ? 'active' : ''}>{customer.status === 'ACTIVE' ? 'فعال' : 'در انتظار'}</em></button>)}</div>}
        <div className="onboarding-pager"><button className="secondary-button" disabled={pageStack.length <= 1 || loading} onClick={() => { const stack = pageStack.slice(0, -1); void loadCustomers(stack.at(-1) || null, stack) }}>قبلی</button><span>صفحهٔ {fa.format(pageStack.length)}</span><button className="secondary-button" disabled={!nextCursor || loading} onClick={() => { if (!nextCursor) return; const stack = [...pageStack, nextCursor]; void loadCustomers(nextCursor, stack) }}>بعدی</button></div>
      </section>
      <section className="onboarding-card customer-detail"><header><div><span className="eyebrow">پرونده و شعب</span><h2>{selected ? selected.displayName || selected.legalName : 'یک مشتری را انتخاب کنید'}</h2></div>{selected?.status === 'PENDING' && <button className="primary-button" onClick={() => { setPendingActivation(selected); setActivationReason('') }}>درخواست فعال‌سازی</button>}</header>
        {!selected ? <p className="onboarding-empty">جزئیات، audit و شعبه‌های مشتری منتخب از API نمایش داده می‌شوند.</p> : <>
          <dl className="onboarding-facts"><div><dt>شناسه</dt><dd dir="ltr">{selected.customerId}</dd></div><div><dt>وضعیت</dt><dd>{selected.status}</dd></div><div><dt>نوع / کانال</dt><dd dir="ltr">{selected.customerType || '—'} · {selected.defaultChannel || '—'}</dd></div><div><dt>ارز</dt><dd dir="ltr">{selected.defaultCurrency || '—'}</dd></div><div><dt>ثبت</dt><dd>{fmtDate(selected.createdAt)}</dd></div></dl>
          <section className="onboarding-subsection"><header><h3>شعبه‌ها</h3><button className="secondary-button" onClick={() => { setBranchForm({ branchId: '', name: '', address: '', latitude: '', longitude: '', zoneId: '', operatingHours: '{}', reason: '' }); setBranchOpen(true) }} disabled={selected.status !== 'ACTIVE'}>افزودن شعبه</button></header>
            {selected.status !== 'ACTIVE' && <p className="onboarding-hint">شعبه فقط پس از فعال‌شدن مشتری از مسیر مجاز API قابل ثبت است.</p>}
            {branches.length === 0 ? <p className="onboarding-empty compact">شعبه‌ای برای این مشتری ثبت نشده است.</p> : <div className="onboarding-branch-list">{branches.map((branch) => <article key={branch.branchId} className="onboarding-branch"><div><strong>{branch.name}</strong><small>{branch.address || 'نشانی ثبت نشده'} · زون {branch.zoneId || '—'}</small><code dir="ltr">{branch.branchId}</code></div><div><button className="text-button" onClick={() => void inspectBranch(branch.branchId)}>جزئیات و audit</button><button className="text-button" onClick={() => void inspectBranch(branch.branchId).then((detail) => { if (detail) editBranch(detail) })} disabled={branch.status !== 'ACTIVE'}>ویرایش</button></div></article>)}</div>}
          </section>
          {branchDetail && <section className="onboarding-subsection"><header><h3>جزئیات شعبه · <span dir="ltr">{branchDetail.branchId}</span></h3></header><dl className="onboarding-facts"><div><dt>نشانی</dt><dd>{branchDetail.address || '—'}</dd></div><div><dt>مختصات</dt><dd dir="ltr">{branchDetail.latitude ?? '—'}, {branchDetail.longitude ?? '—'}</dd></div><div><dt>زون</dt><dd dir="ltr">{branchDetail.zoneId || '—'}</dd></div></dl><pre className="onboarding-json" dir="ltr">{JSON.stringify(branchDetail.operatingHours ?? {}, null, 2)}</pre><h4>سابقهٔ شعبه</h4><AuditList entries={branchAudit} />{branchAuditNextCursor && <button className="secondary-button" onClick={() => void loadBranchAudit(branchDetail.branchId, branchAuditNextCursor, true)}>دریافت رویدادهای بعدی</button>}</section>}
          <section className="onboarding-subsection"><header><h3>دسترسی‌های API</h3></header><p className="onboarding-hint">فقط فرادادهٔ credentialها نمایش داده می‌شود؛ مقدار کلید محرمانه در این API قابل بازیابی نیست.</p>{apiKeys.length ? <div className="onboarding-branches">{apiKeys.map((key) => <article className="onboarding-branch" key={key.keyId}><div><strong dir="ltr">{key.keyId}</strong><span>{key.revokedAt ? 'لغوشده' : new Date(key.expiresAt) <= new Date() ? 'منقضی‌شده' : 'فعال'}</span></div><small>Scopes: <bdi dir="ltr">{key.scopes.length ? key.scopes.join(', ') : '—'}</bdi></small><small>ایجاد: {fmtDate(key.createdAt)} · انقضا: {fmtDate(key.expiresAt)}{key.revokedAt ? ` · لغو: ${fmtDate(key.revokedAt)}` : ''}</small></article>)}</div> : <p className="onboarding-empty compact">برای این مشتری credentialی از API برنگشت.</p>}</section>
          <section className="onboarding-subsection"><header><h3>سابقهٔ مشتری</h3></header><AuditList entries={customerAudit} /></section>
        </>}
      </section>
    </div>}
    {createOpen && <div className="modal-backdrop" role="presentation" onClick={() => !busy && setCreateOpen(false)}><section className="timeline-modal onboarding-modal" role="dialog" aria-modal="true" aria-labelledby="create-customer-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">ثبت در وضعیت Pending</span><h2 id="create-customer-title">مشتری جدید</h2></div><button className="icon-button" onClick={() => setCreateOpen(false)} aria-label="بستن">×</button></header><form className="onboarding-form" onSubmit={(event) => void createCustomer(event)}><label>نام حقوقی<input required maxLength={256} value={createForm.legalName} onChange={(event) => setCreateForm({ ...createForm, legalName: event.target.value })} /></label><label>نام نمایشی<input maxLength={128} value={createForm.displayName} onChange={(event) => setCreateForm({ ...createForm, displayName: event.target.value })} /></label><label>نوع مشتری<input maxLength={64} value={createForm.customerType} onChange={(event) => setCreateForm({ ...createForm, customerType: event.target.value })} /></label><label>کانال پیش‌فرض<input maxLength={64} value={createForm.defaultChannel} onChange={(event) => setCreateForm({ ...createForm, defaultChannel: event.target.value })} /></label><label>ارز پیش‌فرض<input maxLength={3} value={createForm.defaultCurrency} onChange={(event) => setCreateForm({ ...createForm, defaultCurrency: event.target.value })} dir="ltr" /></label><label className="wide">دلیل ثبت<input required minLength={2} maxLength={1000} value={createForm.reason} onChange={(event) => setCreateForm({ ...createForm, reason: event.target.value })} /></label><p className="onboarding-hint wide">ثبت مشتری صرفاً یک رکورد PENDING می‌سازد؛ هیچ دسترسی یا API key صادر نمی‌شود.</p><div className="capacity-actions wide"><button className="primary-button" disabled={busy}>{busy ? 'در حال ثبت…' : 'ثبت Pending'}</button><button type="button" className="secondary-button" onClick={() => setCreateOpen(false)} disabled={busy}>انصراف</button></div></form></section></div>}
    {branchOpen && selected && <div className="modal-backdrop" role="presentation" onClick={() => !busy && setBranchOpen(false)}><section className="timeline-modal onboarding-modal" role="dialog" aria-modal="true" aria-labelledby="branch-form-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">عملیات ممیزی‌شده برای {selected.displayName || selected.legalName}</span><h2 id="branch-form-title">{branchForm.branchId ? 'ویرایش شعبه' : 'ثبت شعبه'}</h2></div><button className="icon-button" onClick={() => setBranchOpen(false)} aria-label="بستن">×</button></header><form className="onboarding-form" onSubmit={(event) => void saveBranch(event)}><label>نام شعبه<input required maxLength={128} value={branchForm.name} onChange={(event) => setBranchForm({ ...branchForm, name: event.target.value })} /></label><label>زون<input maxLength={128} value={branchForm.zoneId} onChange={(event) => setBranchForm({ ...branchForm, zoneId: event.target.value })} /></label><label className="wide">نشانی<input maxLength={500} value={branchForm.address} onChange={(event) => setBranchForm({ ...branchForm, address: event.target.value })} /></label><label>عرض جغرافیایی<input type="number" min="-90" max="90" step="any" value={branchForm.latitude} onChange={(event) => setBranchForm({ ...branchForm, latitude: event.target.value })} dir="ltr" /></label><label>طول جغرافیایی<input type="number" min="-180" max="180" step="any" value={branchForm.longitude} onChange={(event) => setBranchForm({ ...branchForm, longitude: event.target.value })} dir="ltr" /></label><label className="wide">ساعات کاری (JSON object)<textarea rows={4} value={branchForm.operatingHours} onChange={(event) => setBranchForm({ ...branchForm, operatingHours: event.target.value })} dir="ltr" /></label><label className="wide">دلیل الزامی<input required minLength={2} maxLength={1000} value={branchForm.reason} onChange={(event) => setBranchForm({ ...branchForm, reason: event.target.value })} /></label><div className="capacity-actions wide"><button className="primary-button" disabled={busy}>{busy ? 'در حال ذخیره…' : 'ذخیره از طریق API'}</button><button type="button" className="secondary-button" onClick={() => setBranchOpen(false)} disabled={busy}>انصراف</button></div></form></section></div>}
    {pendingActivation && <div className="modal-backdrop" role="presentation" onClick={() => !busy && setPendingActivation(null)}><section className="timeline-modal onboarding-confirm" role="dialog" aria-modal="true" aria-labelledby="activate-customer-title" onClick={(event) => event.stopPropagation()}><header><div><span className="eyebrow">تغییر وضعیت مشتری</span><h2 id="activate-customer-title">تأیید فعال‌سازی</h2></div><button className="icon-button" onClick={() => setPendingActivation(null)} aria-label="بستن">×</button></header><p>API وضعیت «{pendingActivation.displayName || pendingActivation.legalName}» را از PENDING به ACTIVE تغییر می‌دهد و audit ثبت می‌کند. این کار دسترسی یا credential صادر نمی‌کند.</p><label>دلیل ممیزی‌شده<input value={activationReason} onChange={(event) => setActivationReason(event.target.value)} minLength={2} maxLength={1000} required /></label><div className="capacity-actions"><button className="primary-button" disabled={busy || activationReason.trim().length < 2} onClick={() => void activateCustomer()}>تأیید و فعال‌سازی</button><button className="secondary-button" disabled={busy} onClick={() => setPendingActivation(null)}>انصراف</button></div></section></div>}
  </div>
}

function AuditList({ entries }: { entries: Audit[] }) {
  return entries.length ? <div className="onboarding-audit">{entries.map((entry, index) => <article key={entry.auditId || `${entry.action}-${entry.occurredAt}-${index}`}><strong>{entry.action}</strong><span>{fmtDate(entry.occurredAt)}</span><small>{entry.actorId} · {entry.reason}</small></article>)}</div> : <p className="onboarding-empty compact">سابقه‌ای از API برنگشت.</p>
}
