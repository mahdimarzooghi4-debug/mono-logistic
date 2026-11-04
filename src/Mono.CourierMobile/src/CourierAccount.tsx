type AccountProps = { onNotice: (message: string) => void }

function AccountRow({ label, value, valueClass = '' }: { label: string; value: string; valueClass?: string }) {
  return <div className="account-data-row"><strong className={valueClass}>{value}</strong><span>{label}</span></div>
}

export default function CourierAccount({ onNotice }: AccountProps) {
  return <section className="content-scroll account-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/avatar.svg" alt="" />
      <div className="profile-copy"><strong>حساب کاربری</strong><small>پروفایل و تنظیمات پیک</small></div>
    </header>

    <section className="account-profile-card">
      <div className="account-profile-head">
        <img src="/figma/avatar.svg" alt="" />
        <div><strong>رضا احمدی</strong><small>CR-۲۸۴ • ۰۹۱۲ ۳۴۵ ۶۷۸۹</small></div>
      </div>
      <div className="account-status-row">
        <div><small>وضعیت</small><strong className="account-good">فعال</strong></div>
        <div><small>هویت</small><strong className="account-good">تأیید</strong></div>
        <div><small>اعتماد</small><strong className="account-good">تأیید</strong></div>
      </div>
    </section>

    <section className="account-details-card">
      <h1>مشخصات عملیاتی</h1>
      <AccountRow label="شیوه حمل" value="موتورسیکلت" valueClass="account-teal" />
      <AccountRow label="وسیله" value="VEH-REG-۰۷۸" valueClass="account-teal" />
      <AccountRow label="مناطق مجاز" value="مرکزی، شمال" />
      <AccountRow label="ماموریت همزمان" value="۳" />
    </section>

    <section className="account-documents-card">
      <h2>مدارک و وضعیت حساب</h2>
      <AccountRow label="مدارک وسیله" value="معتبر تا ۱۴۰۶/۰۹" valueClass="account-good" />
      <AccountRow label="تضمین" value="تأییدشده" valueClass="account-good" />
      <AccountRow label="حساب تسویه" value="تأییدشده" valueClass="account-good" />
    </section>

    <section className="account-actions-card" aria-label="پشتیبانی و تنظیمات">
      <button onClick={() => onNotice('برای پشتیبانی حساب پیک با مرکز عملیات تماس بگیرید.')}>
        <span><strong>پشتیبانی</strong><small>پیگیری درباره مأموریت یا حساب</small></span><b aria-hidden="true">‹</b>
      </button>
      <button onClick={() => onNotice('تنظیمات اعلان و دسترسی در نسخه آزمایشی نمایشی است.')}>
        <span><strong>تنظیمات</strong><small>اعلان‌ها و دسترسی برنامه</small></span><b aria-hidden="true">‹</b>
      </button>
      <button className="account-logout" onClick={() => onNotice('خروج از حساب در نسخه آزمایشی غیرفعال است.')}>
        <span><strong>خروج از حساب</strong><small>خروج امن از این اپ</small></span><b aria-hidden="true">‹</b>
      </button>
    </section>
  </section>
}
