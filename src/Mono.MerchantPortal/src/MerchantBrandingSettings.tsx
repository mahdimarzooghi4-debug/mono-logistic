import { useEffect, useRef, useState } from 'react'
import type { ChangeEvent } from 'react'

type SettingsTab = 'integrations' | 'notifications' | 'members' | 'workspace' | 'branding'
type BrandValues = { brandName: string; workspaceName: string; primary: string; accent: string }

const sampleValues: BrandValues = { brandName: 'فروشگاه سپهر', workspaceName: 'ارسال و لجستیک', primary: '#7B4DFF', accent: '#A88BFF' }
const palette = ['#7B4DFF', '#00A8C6', '#D9468C', '#2166A5', '#168A52', '#B46A00']
const lockedColors = [
  { color: '#168A52', title: 'رنگ موفقیت', description: 'تحویل، وضعیت سالم و تأیید' },
  { color: '#B46A00', title: 'رنگ هشدار', description: 'ریسک، انتظار و نیازمند توجه' },
  { color: '#C83A3A', title: 'رنگ خطا', description: 'حادثه، استثنا و خطا' },
  { color: '#00A8C6', title: 'Powered by Mono', description: 'شناسهٔ زیرساخت لجستیک مونو' },
]
const tabLabels: Array<[SettingsTab, string]> = [
  ['branding', 'هویت بصری'], ['workspace', 'Workspace'], ['members', 'اعضا و دسترسی'], ['notifications', 'اعلان‌ها'], ['integrations', 'یکپارچه‌سازی'],
]
const tabCopy: Record<SettingsTab, [string, string]> = {
  integrations: ['یکپارچه‌سازی', 'اتصال API، مدیریت Webhook و پایش تحویل رویدادها'],
  notifications: ['تنظیمات اعلان‌ها', 'کانال‌ها، رویدادها و دریافت‌کنندگان اعلان‌ها'],
  members: ['اعضا و دسترسی', 'مدیریت اعضای Workspace، نقش‌ها و محدودهٔ دسترسی'],
  workspace: ['تنظیمات Workspace', 'پیکربندی فضای کاری، داده‌ها و پیش‌فرض‌های عملیاتی'],
  branding: ['تنظیمات هویت بصری', 'ظاهر Workspace خودت را با برند کسب‌وکارت هماهنگ کن.'],
}

export default function MerchantBrandingSettings({
  mode,
  onTabChange,
  onDemoSave,
  onInvite,
  onAddEndpoint,
}: {
  mode: 'demo' | 'connected'
  onTabChange?: (tab: SettingsTab) => void
  onDemoSave?: () => void
  onInvite?: () => void
  onAddEndpoint?: () => void
}) {
  const isDemo = mode === 'demo'
  const [activeTab, setActiveTab] = useState<SettingsTab>('branding')
  const [values, setValues] = useState<BrandValues>(isDemo ? sampleValues : { brandName: '', workspaceName: '', primary: sampleValues.primary, accent: sampleValues.accent })
  const [logoUrl, setLogoUrl] = useState<string | null>(null)
  const [logoError, setLogoError] = useState('')
  const [savedPreview, setSavedPreview] = useState(false)
  const logoUrlRef = useRef<string | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => () => {
    if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current)
  }, [])

  function setValue<K extends keyof BrandValues>(key: K, value: BrandValues[K]) {
    setValues((current) => ({ ...current, [key]: value }))
    setSavedPreview(false)
  }

  function resetDraft() {
    setValues(isDemo ? sampleValues : { brandName: '', workspaceName: '', primary: sampleValues.primary, accent: sampleValues.accent })
    setLogoError('')
    setSavedPreview(false)
    if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current)
    logoUrlRef.current = null
    setLogoUrl(null)
    if (fileRef.current) fileRef.current.value = ''
  }

  function chooseLogo(event: ChangeEvent<HTMLInputElement>) {
    setLogoError('')
    const file = event.currentTarget.files?.[0]
    if (!file) return
    const allowed = ['image/png', 'image/jpeg', 'image/svg+xml']
    if (!allowed.includes(file.type)) {
      setLogoError('فقط فایل PNG، JPG یا SVG انتخاب کنید.')
      event.currentTarget.value = ''
      return
    }
    if (file.size > 2 * 1024 * 1024) {
      setLogoError('حجم فایل باید کمتر از ۲ مگابایت باشد.')
      event.currentTarget.value = ''
      return
    }
    if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current)
    logoUrlRef.current = URL.createObjectURL(file)
    setLogoUrl(logoUrlRef.current)
    setSavedPreview(false)
  }

  function updateHex(key: 'primary' | 'accent', raw: string) {
    const normalized = raw.trim().toUpperCase()
    if (/^#[0-9A-F]{6}$/.test(normalized)) setValue(key, normalized)
  }

  return <section className="merchant-branding-page" dir="rtl">
    <header className="branding-page-header">
      <div><span className="eyebrow">{activeTab === 'branding' ? `هویت Workspace${isDemo ? ' · پیش‌نمایش' : ''}` : 'Workspace · پیش‌نمایش'}</span><h1>{tabCopy[activeTab][0]}</h1><p>{tabCopy[activeTab][1]}</p></div>
      <div className="branding-header-actions">{activeTab === 'members' ? <button className="primary-button" onClick={onInvite}>دعوت عضو</button> : activeTab === 'integrations' ? <><button className="secondary-button" type="button">مستندات API</button><button className="primary-button" onClick={onAddEndpoint}>افزودن Endpoint</button></> : activeTab === 'branding' ? <><button className="secondary-button" onClick={resetDraft}>بازنشانی</button><button className="primary-button" disabled={!isDemo || savedPreview} title={!isDemo ? 'ذخیره‌سازی هویت بصری هنوز در API حساب موجود نیست.' : undefined} onClick={() => { setSavedPreview(true); onDemoSave?.() }}>ذخیره تغییرات</button></> : <button className="primary-button" disabled={!isDemo} title={!isDemo ? 'ذخیرهٔ تنظیمات از API هنوز پشتیبانی نمی‌شود.' : undefined} onClick={() => { setSavedPreview(true); onDemoSave?.() }}>ذخیره تغییرات</button>}</div>
    </header>
    <nav className="settings-tabs branding-tabs" aria-label="بخش‌های تنظیمات Workspace" role="tablist">
      {tabLabels.map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={id === activeTab} className={id === activeTab ? 'selected' : ''} disabled={id !== 'branding' && !isDemo && !onTabChange} onClick={() => { setActiveTab(id); if (id !== 'branding' && !isDemo) onTabChange?.(id) }}>{label}</button>)}
    </nav>
    {isDemo && activeTab !== 'branding' ? <DemoSettingsTab tab={activeTab} /> : <>
    {!isDemo && <p className="branding-capability-note">تنظیمات جاری از API هویت بصری دریافت نشده‌اند. مقادیر زیر فقط پیش‌نمایش موقت مرورگرند و دکمهٔ ذخیره تا آماده‌شدن API غیرفعال است.</p>}
    {savedPreview && isDemo && <p className="branding-preview-saved" role="status">تغییرهای این پیش‌نمایش در همین صفحه ثبت شدند.</p>}
    <div className="branding-layout">
      <aside className="branding-live-preview">
        <div className="branding-preview-title"><span className="branding-preview-pill">Preview</span><h2>پیش‌نمایش زنده</h2></div>
        <div className="branding-mock-workspace" style={{ '--brand-primary': values.primary, '--brand-accent': values.accent } as React.CSSProperties}>
          <aside className="branding-mock-sidebar">
            <div className="branding-mock-identity"><div className="branding-mock-logo">{logoUrl ? <img src={logoUrl} alt="لوگوی انتخاب‌شده" /> : <span>{(values.brandName || 'س').trim().slice(0, 1)}</span>}</div><strong>{values.brandName || 'نام کسب‌وکار'}</strong><small>Powered by Mono</small></div>
            <nav aria-label="پیش‌نمایش منو"><span>داشبورد</span><span>ارسال جدید</span><span>ارسال‌ها</span><span>رهگیری</span><span className="current">تنظیمات</span></nav>
          </aside>
          <div className="branding-mock-content"><h3>داشبورد کسب‌وکار</h3><small>{values.workspaceName || 'عنوان Workspace'}</small><button type="button">ارسال جدید</button><article><strong>ارسال فعال</strong><div><span>در مسیر</span><span>تحویل‌شده</span><span>هشدار</span></div></article><article className="branding-mock-rows"><i /><i /><i /><i /></article></div>
        </div>
        <div className="branding-preview-footnote"><span>تغییرات فقط در پیش‌نمایش دیده می‌شوند.</span><strong>{isDemo ? 'ذخیره، دادهٔ واقعی را تغییر نمی‌دهد.' : 'پیش‌نمایش محلی؛ تنظیمات واقعی از API نیامده‌اند.'}</strong></div>
      </aside>
      <div className="branding-form-column">
        <section className="branding-settings-card">
          <div className="branding-card-heading"><div><h2>هویت برند</h2><p>این اطلاعات در Header و Sidebar پنل Workspace نمایش داده می‌شوند.</p></div></div>
          <div className="branding-identity-grid">
            <div className="branding-logo-control"><div className="branding-logo-preview" style={{ '--brand-primary': values.primary, '--brand-accent': values.accent } as React.CSSProperties}>{logoUrl ? <img src={logoUrl} alt="لوگوی انتخاب‌شده" /> : <span>{(values.brandName || 'س').trim().slice(0, 1)}</span>}</div><div className="branding-logo-actions"><strong>لوگوی کسب‌وکار</strong><small>PNG، JPG یا SVG · حداکثر ۲ مگابایت</small><div><button className="secondary-button" type="button" onClick={() => fileRef.current?.click()}>جایگزینی لوگو</button><button className="danger-outline-button" type="button" disabled={!logoUrl} onClick={() => { if (logoUrlRef.current) URL.revokeObjectURL(logoUrlRef.current); logoUrlRef.current = null; setLogoUrl(null); if (fileRef.current) fileRef.current.value = '' }}>حذف لوگو</button></div><input ref={fileRef} className="sr-only" type="file" accept="image/png,image/jpeg,image/svg+xml" onChange={chooseLogo} aria-label="انتخاب لوگوی کسب‌وکار" />{logoError && <small className="branding-error" role="alert">{logoError}</small>}</div></div>
            <label className="branding-field">نام تجاری<input value={values.brandName} maxLength={80} onChange={(event) => setValue('brandName', event.target.value)} placeholder={isDemo ? 'فروشگاه سپهر' : 'هنوز از API دریافت نشده'} /></label>
            <label className="branding-field">عنوان Workspace<input value={values.workspaceName} maxLength={100} onChange={(event) => setValue('workspaceName', event.target.value)} placeholder={isDemo ? 'ارسال و لجستیک' : 'هنوز از API دریافت نشده'} /></label>
          </div>
        </section>
        <section className="branding-settings-card">
          <div className="branding-card-heading"><div><h2>رنگ‌های برند</h2><p>رنگ‌های اصلی و تأکیدی روی پوسته و اجزای تعاملی اثر می‌گذارند.</p></div></div>
          <div className="branding-color-grid">
            <label className="branding-color-field"><span>رنگ اصلی</span><div><input type="color" value={values.primary} onChange={(event) => setValue('primary', event.target.value.toUpperCase())} aria-label="انتخاب رنگ اصلی" /><input className="branding-hex-input" value={values.primary} maxLength={7} onChange={(event) => updateHex('primary', event.target.value)} aria-label="کد رنگ اصلی" /></div></label>
            <label className="branding-color-field"><span>رنگ Accent</span><div><input type="color" value={values.accent} onChange={(event) => setValue('accent', event.target.value.toUpperCase())} aria-label="انتخاب رنگ Accent" /><input className="branding-hex-input" value={values.accent} maxLength={7} onChange={(event) => updateHex('accent', event.target.value)} aria-label="کد رنگ Accent" /></div></label>
          </div>
          <div className="branding-palette" aria-label="رنگ‌های پیشنهادی">{palette.map((color) => <button key={color} type="button" style={{ backgroundColor: color }} aria-label={`انتخاب رنگ ${color}`} aria-pressed={values.primary === color} onClick={() => setValue('primary', color)} />)}</div>
          <small className="branding-form-hint">برای واردکردن کد، قالب HEX شش‌رقمی مانند ‎#7B4DFF را بنویسید.</small>
        </section>
        <section className="branding-settings-card branding-locked-card">
          <div className="branding-card-heading"><div><h2>موارد غیرقابل تغییر</h2><p>برای حفظ معنا و یکسان‌بودن وضعیت‌ها، این رنگ‌ها ثابت هستند.</p></div><span className="branding-lock-badge">قفل</span></div>
          <div className="branding-locked-list">{lockedColors.map((item) => <div key={item.title}><i style={{ backgroundColor: item.color }} /><div><strong>{item.title}</strong><small>{item.description}</small></div><span>قفل</span></div>)}</div>
        </section>
      </div>
    </div>
    </>}
  </section>
}

function DemoSettingsTab({ tab }: { tab: Exclude<SettingsTab, 'branding'> }) {
  return <div className={`demo-settings-content demo-settings-${tab}`}>
    {tab === 'workspace' && <div className="demo-settings-columns"><div className="demo-settings-stack"><section className="panel"><h2>مشخصات Workspace</h2><div className="demo-setting-grid"><Setting label="شناسه Workspace" value="WS-SEPEHR-001" /><Setting label="نام Workspace" value="فروشگاه سپهر" /><Setting label="کشور" value="ایران" /><Setting label="شهر پیش‌فرض" value="تهران" /><Setting label="نشانی تحویل" value="ونک، خیابان ملاصدرا، پلاک ۲۱" /></div></section><section className="panel"><h2>تنظیمات منطقه‌ای</h2><div className="demo-setting-grid"><Setting label="زبان" value="فارسی" /><Setting label="منطقه زمانی" value="Asia/Tehran" /><Setting label="واحد پول" value="تومان" /><Setting label="تقویم تاریخ" value="شمسی" /></div></section><section className="panel"><h2>داده و نگهداری</h2><Toggle label="ذخیره تاریخچه ارسال‌ها برای گزارش‌گیری" /><Toggle label="نگهداری لاگ فعالیت اعضا" /><Toggle label="نمایش اطلاعات تماس گیرنده فقط برای نقش‌های مجاز" /></section></div><div className="demo-settings-stack"><section className="panel"><h2>پیش‌فرض‌های عملیاتی</h2><div className="demo-setting-grid"><Setting label="سرویس پیش‌فرض" value="ارسال شهری" /><Setting label="شعاع سرویس" value="۵ کیلومتر" /><Setting label="زمان آمادگی پیش‌فرض" value="۱۰ دقیقه" /><Setting label="حد انتظار پیک" value="۳ دقیقه" /><Setting label="سقف Batch" value="۵ مأموریت" /></div></section><section className="panel"><h2>رفتار Workspace</h2><Toggle label="نمایش تعهد تحویل قبل از ثبت نهایی ارسال" /><Toggle label="نیاز به تأیید کاربر قبل از ثبت کرایه" /><Toggle label="فعال‌سازی رهگیری زنده برای سفارش‌ها" /><Toggle label="نمایش وضعیت استاندارد خدمت در لیست‌ها" /></section><section className="panel"><h2>خلاصه اعمال تنظیمات</h2><p className="data-note">تغییرات این پیش‌نمایش فقط روی Workspace نمونه اثر می‌گذارند.</p></section></div></div>}
    {tab === 'members' && <DemoMembersSettings />}
    {tab === 'notifications' && <DemoNotificationSettings />}
    {tab === 'integrations' && <DemoIntegrationSettings />}
  </div>
}

function Setting({ label, value }: { label: string; value: string }) { return <div className="demo-setting"><small>{label}</small><strong>{value}</strong></div> }
function Toggle({ label, checked = true }: { label: string; checked?: boolean }) { return <label className="demo-toggle"><input type="checkbox" defaultChecked={checked} /><span>{label}</span></label> }

function DemoMembersSettings() {
  const members = [
    ['مهدی مرادی', '۰۹۱۲ ۳۴۵ ۸۷۱۱', 'همه', 'مالک', 'امروز ۱۳:۳۱', 'فعال'],
    ['سارا حسینی', 'admin@sepehr.ir', 'همه', 'ادمین', 'امروز ۱۰:۵۸', 'فعال'],
    ['علی رضایی', '۰۹۱۲ ۲۲۰ ۸۷۱۱', 'ونک + جردن', 'اپراتور', 'امروز ۱۰:۴۲', 'فعال'],
    ['مریم نادری', 'finance@sepehr.ir', 'همه', 'مالی', 'دیروز ۱۸:۲۴', 'فعال'],
    ['رضا شریفی', 'ops2@sepehr.ir', 'محدوده', 'اپراتور', '—', 'دعوت شده'],
  ]
  const permissions = [
    ['مجاز', 'ثبت و ویرایش ارسال'],
    ['مجاز', 'مشاهده رهگیری زنده'],
    ['مجاز', 'درخواست لغو قبل از Handoff'],
    ['محدود', 'ویرایش هویت بصری'],
    ['محدود', 'مدیریت اعضا'],
  ]
  return <><div className="demo-member-kpis"><Setting label="دعوت در انتظار" value="۲" /><Setting label="مالی" value="۱" /><Setting label="اپراتور" value="۳" /><Setting label="ادمین" value="۲" /><Setting label="اعضای فعال" value="۸" /></div><div className="demo-settings-columns"><div className="demo-settings-stack"><section className="panel demo-member-table-panel"><div className="demo-table-scroll"><table className="demo-member-table"><thead><tr><th>عضو</th><th>موبایل/ایمیل</th><th>شعب</th><th>نقش</th><th>آخرین فعالیت</th><th>وضعیت</th></tr></thead><tbody>{members.map((row) => <tr key={row[0]}>{row.map((cell,index) => <td key={index}>{index===5?<span className={`badge ${cell==='فعال'?'success':'warning'}`}>{cell}</span>:cell}</td>)}</tr>)}</tbody></table></div></section><section className="panel demo-member-roles-panel"><h2>ماتریس نقش‌ها</h2><div className="demo-role-matrix">{[['مالک','همهٔ دسترسی‌ها'],['ادمین','مدیریت Workspace'],['اپراتور','ارسال و رهگیری'],['مالی','صورتحساب و گزارش'],['مشاهده‌گر','فقط مشاهده']].map(([r,d])=><div key={r}><strong>{r}</strong><small>{d}</small></div>)}</div></section></div><div className="demo-settings-stack"><section className="panel demo-member-summary-panel"><div className="demo-member-selected-head"><span className="badge success">فعال</span><h2>علی رضایی</h2></div><Setting label="نقش" value="اپراتور"/><Setting label="موبایل" value="۰۹۱۲ ۲۲۰ ۸۷۱۱"/><Setting label="آخرین فعالیت" value="امروز ۱۰:۴۲"/></section><section className="panel demo-member-scope-panel"><h2>محدوده دسترسی</h2><Setting label="شعب" value="ونک + جردن"/><Setting label="ارسال جدید" value="مجاز"/><Setting label="صورتحساب" value="فقط مشاهده"/><Setting label="تنظیمات Workspace" value="غیرمجاز"/></section><section className="panel demo-member-permissions-panel"><h2>مجوزهای کلیدی</h2><div className="demo-member-permissions">{permissions.map(([status, label])=><div className="demo-member-permission" key={label}><span className={`badge ${status==='مجاز'?'success':'muted'}`}>{status}</span><span>{label}</span></div>)}</div></section><section className="panel demo-member-actions-panel"><div className="demo-member-actions"><button className="primary-button">ویرایش دسترسی</button><button className="secondary-button">لغو دسترسی</button></div><p>همهٔ تغییرات دسترسی در Audit ثبت می‌شوند.</p></section></div></div></>
}

function DemoNotificationSettings() {
  const events=['ثبت ارسال','تخصیص پیک','دریافت از شعبه','تحویل به گیرنده','ریسک استاندارد خدمت','حادثه / استثنا','لغو ارسال','صدور صورتحساب','موعد پرداخت']
  return <div className="demo-settings-columns"><div className="demo-settings-stack"><section className="panel"><h2>کانال‌های اعلان</h2><div className="demo-channel-grid">{[['داخل پنل','فعال'],['پیامک','فعال'],['ایمیل','فعال'],['Webhook','متصل']].map(([c,v])=><div key={c}><strong>{c}</strong><small>{v}</small></div>)}</div></section><section className="panel demo-event-panel"><h2>رویدادها و کانال‌ها</h2>{events.map((e,i)=><div className="demo-event-row" key={e}><strong>{e}</strong><span className="chosen">داخل پنل</span><span className={i>=4?'chosen':''}>پیامک</span><span>ایمیل</span></div>)}</section><section className="panel"><h2>خلاصه ارسال اعلان</h2><p className="data-note">تاریخچهٔ نمونه: اعلان تحویل، امروز ساعت ۱۲:۵۸</p><button className="secondary-button">مشاهده تاریخچه اعلان‌ها</button></section></div><div className="demo-settings-stack"><section className="panel"><h2>دریافت‌کنندگان</h2><Toggle label="ادمین‌های Workspace"/><Toggle label="اپراتورهای شعب مرتبط"/><Toggle label="تیم مالی"/><Toggle label="مشاهده‌گرها" checked={false}/></section><section className="panel"><h2>ساعات سکوت</h2><Toggle label="فعال‌سازی ساعات سکوت"/><div className="demo-setting-grid"><Setting label="شروع" value="۲۲:۰۰"/><Setting label="پایان" value="۰۷:۰۰"/></div></section><section className="panel"><h2>Escalation</h2><Toggle label="ارسال SMS برای Incident بحرانی"/><Toggle label="تکرار هشدار بعد از ۱۰ دقیقه"/><Toggle label="ارسال ایمیل خلاصه پایان روز"/></section><section className="panel"><h2>قاعده</h2><p className="data-note">اعلان‌ها و گیرندگان این صفحه برای همین Workspace اعمال می‌شوند؛ داده‌های رویداد و فعالیت نمونه‌اند.</p></section></div></div>
}

function DemoIntegrationSettings() {
  return <><div className="demo-member-kpis"><Setting label="خطای باز" value="۱"/><Setting label="آخرین Event" value="۱۲:۴۸"/><Setting label="موفقیت ۲۴ساعت" value="۹۹٫۲٪"/><Setting label="Webhook" value="۳ فعال"/><Setting label="API" value="فعال"/></div><div className="demo-settings-columns"><div className="demo-settings-stack"><section className="panel"><h2>دسترسی API</h2><Setting label="Environment" value="Production"/><Setting label="API Key" value="••••••••••••••••••۳۸۴A"/><div className="demo-settings-actions"><button className="secondary-button">چرخش کلید</button><button className="secondary-button">کپی شناسه</button></div></section><section className="panel"><h2>Webhook Endpoint</h2><Setting label="URL" value="https://api.sepehr.ir/hooks/mono"/><Setting label="وضعیت" value="فعال"/><Setting label="Secret" value="ذخیره‌شده و پنهان"/><div className="demo-chip-row"><span>mission.updated</span><span>delivery.completed</span><span>incident.created</span><span>billing.created</span></div><div className="demo-settings-actions"><button className="secondary-button">تست Endpoint</button><button className="secondary-button">ویرایش رویدادها</button></div></section></div><div className="demo-settings-stack"><section className="panel"><h2>سلامت Webhook</h2><Setting label="آخرین پاسخ" value="۲۰۰ · ۱۲:۴۸"/><Setting label="Latency" value="۱۸۰ ms"/><Setting label="Retry باز" value="۱"/><Setting label="آخرین تحویل" value="۱۲:۴۸"/></section><section className="panel demo-webhook-table"><h2>تحویل‌های اخیر</h2>{['delivery.completed','mission.updated','incident.created','delivery.completed'].map((x,i)=><div key={`${x}-${i}`}><span>{x}</span><b>{i===2?'خطا':'موفق'}</b></div>)}</section><section className="panel"><h2>سیاست Retry</h2><Setting label="تعداد تلاش" value="۵"/><Setting label="Backoff" value="۱، ۲، ۴، ۸ دقیقه"/><Setting label="حداکثر پنجره" value="۲۴ ساعت"/></section></div></div></>
}
