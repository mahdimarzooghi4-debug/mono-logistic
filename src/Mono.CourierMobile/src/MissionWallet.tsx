import { useState } from 'react'

type WalletProps = { onNotice: (message: string) => void }
type Transaction = { id: string; title: string; description: string; amount: number; type: 'credit' | 'debit' }

const initialTransactions: Transaction[] = [
  { id: 'MIS-۱۰۴۹۸', title: 'MIS-۱۰۴۹۸', description: 'تحویل موفق • ۱۱:۰۶', amount: 24920, type: 'credit' },
  { id: 'withdrawal-1030', title: 'برداشت به شبا', description: 'امروز • ۱۰:۳۰', amount: 200000, type: 'debit' },
  { id: 'MIS-۱۰۴۹۱', title: 'MIS-۱۰۴۹۱', description: 'تحویل موفق • ۱۰:۱۲', amount: 22250, type: 'credit' },
]
const currency = new Intl.NumberFormat('fa-IR')

export default function MissionWallet({ onNotice }: WalletProps) {
  const [balance, setBalance] = useState(511420)
  const [withdrawalsToday, setWithdrawalsToday] = useState(1)
  const [transactions, setTransactions] = useState(initialTransactions)
  const [withdrawalOpen, setWithdrawalOpen] = useState(false)
  const [amount, setAmount] = useState('')
  const [error, setError] = useState('')
  const todayNet = 267000

  function submitWithdrawal() {
    const requestedAmount = Number(amount.replace(/[٬,]/g, '').replace(/[۰-۹]/g, digit => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(digit))))
    if (!Number.isInteger(requestedAmount) || requestedAmount <= 0) {
      setError('مبلغ برداشت را به‌درستی وارد کن.')
      return
    }
    if (requestedAmount > balance) {
      setError('مبلغ برداشت از موجودی قابل برداشت بیشتر است.')
      return
    }
    if (withdrawalsToday >= 3) {
      setError('سقف ۳ برداشت روزانه تکمیل شده است.')
      return
    }
    setBalance(current => current - requestedAmount)
    setWithdrawalsToday(current => current + 1)
    setTransactions(current => [{ id: `withdrawal-${Date.now()}`, title: 'برداشت به شبا', description: 'امروز • همین الان', amount: requestedAmount, type: 'debit' }, ...current])
    setWithdrawalOpen(false)
    setAmount('')
    setError('')
    onNotice('درخواست برداشت نمایشی ثبت شد؛ به بک‌اند بانکی متصل نیست.')
  }

  return <section className="content-scroll wallet-content" dir="rtl">
    <header className="profile-header">
      <img className="avatar" src="/figma/avatar.svg" alt="" />
      <div className="profile-copy"><strong>درآمد و کیف پول</strong><small>خالص درآمد و برداشت</small></div>
    </header>

    <section className="wallet-hero-card">
      <div className="wallet-section-heading"><span className="withdrawable-badge">قابل برداشت</span><h1>موجودی کیف پول</h1></div>
      <strong className="wallet-balance">{currency.format(balance)} تومان</strong>
      <p>مبالغ مأموریت‌های تکمیل‌شده بلافاصله به موجودی قابل برداشت اضافه می‌شوند.</p>
    </section>

    <section className="wallet-earnings-summary">
      <div><small>خالص امروز</small><strong className="wallet-green">{currency.format(todayNet)}</strong></div>
      <div><small>این هفته</small><strong>{currency.format(1486000)}</strong></div>
      <div><small>در انتظار</small><strong>۰</strong></div>
    </section>

    <section className="wallet-withdrawal-card">
      <div className="wallet-section-heading"><span className="withdrawals-count">{currency.format(withdrawalsToday)} از ۳</span><h2>برداشت امروز</h2></div>
      <p className="withdrawal-limit">حداکثر ۳ برداشت در هر روز</p>
      <div className="wallet-bank-row"><strong>IR ۸۲۰۱••••••۴۵۶۷</strong><span>شبای مقصد</span></div>
      <button className="wallet-withdraw-button" onClick={() => { setWithdrawalOpen(true); setError('') }} disabled={withdrawalsToday >= 3}>برداشت وجه</button>
    </section>

    <section className="wallet-transactions-card">
      <div className="wallet-section-heading"><span className="today-badge">امروز</span><h2>آخرین تراکنش‌ها</h2></div>
      <div className="wallet-transactions-list">
        {transactions.slice(0, 3).map(tx => <div className="wallet-transaction" key={tx.id}>
          <strong className={tx.type === 'credit' ? 'credit-amount' : 'debit-amount'}>{tx.type === 'credit' ? '+' : '−'}{currency.format(tx.amount)} تومان</strong>
          <div><b>{tx.title}</b><small>{tx.description}</small></div>
          <img src={tx.type === 'credit' ? '/figma/wallet-credit-dot.svg' : '/figma/wallet-debit-dot.svg'} alt="" />
        </div>)}
      </div>
    </section>

    {withdrawalOpen && <div className="wallet-modal-backdrop" role="presentation" onClick={() => setWithdrawalOpen(false)}>
      <section className="wallet-withdrawal-dialog" role="dialog" aria-modal="true" aria-labelledby="withdrawal-title" onClick={event => event.stopPropagation()}>
        <button className="wallet-modal-close" aria-label="بستن برداشت" onClick={() => setWithdrawalOpen(false)}>×</button>
        <h2 id="withdrawal-title">درخواست برداشت وجه</h2>
        <p>موجودی قابل برداشت: <strong>{currency.format(balance)} تومان</strong></p>
        <label className="withdrawal-amount-field">
          <span>مبلغ برداشت (تومان)</span>
          <input aria-label="مبلغ برداشت" inputMode="numeric" value={amount} onChange={event => { setAmount(event.target.value); setError('') }} placeholder="مثلاً ۲۰۰۰۰۰" />
        </label>
        <div className="withdrawal-destination">واریز به شبا <strong>IR ۸۲۰۱••••••۴۵۶۷</strong></div>
        {error && <p className="withdrawal-error" role="alert">{error}</p>}
        <button className="wallet-withdraw-confirm" onClick={submitWithdrawal}>تأیید برداشت نمایشی</button>
      </section>
    </div>}
  </section>
}
