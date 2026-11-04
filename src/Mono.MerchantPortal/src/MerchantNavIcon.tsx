import newDelivery from './assets/figma-dashboard/icon-1.svg'
import tracking from './assets/figma-dashboard/icon-2.svg'
import branches from './assets/figma-dashboard/icon-3.svg'
import billing from './assets/figma-dashboard/icon-4.svg'
import settings from './assets/figma-dashboard/icon-5.svg'

export type MerchantNavId = 'dashboard' | 'create' | 'deliveries' | 'tracking' | 'branches' | 'billing' | 'settings'

const assets: Partial<Record<MerchantNavId, string>> = {
  create: newDelivery,
  tracking,
  branches,
  billing,
  settings,
}

export default function MerchantNavIcon({ id }: { id: MerchantNavId }) {
  const asset = assets[id]
  if (asset) return <img className="merchant-nav-svg" src={asset} alt="" aria-hidden="true" />
  if (id === 'dashboard') return <span className="merchant-nav-dashboard" aria-hidden="true"><i /><i /><i /><i /></span>
  return <span className="merchant-nav-deliveries" aria-hidden="true"><i /><i /><i /></span>
}
