import { useState, useRef, useEffect } from 'react'
import { Bell, ChevronDown } from 'lucide-react'
import { Badge } from '@/components/ui/badge'

interface NavbarProps {
  upcomingPaymentsCount?: number
  pendingTransfers?: number
  pendingReconciliations?: number
}

const navLink = "!text-gray-300 hover:!text-white hover:bg-gray-800 px-2.5 py-1.5 rounded-md transition-colors no-underline text-sm"
const navLinkBadge = `${navLink} inline-flex items-center gap-1.5`

export function Navbar({ upcomingPaymentsCount = 0, pendingTransfers = 0, pendingReconciliations = 0 }: NavbarProps) {
  const [reviewOpen, setReviewOpen] = useState(false)
  const dropdownRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setReviewOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  const reviewBadgeCount = pendingTransfers + pendingReconciliations

  return (
    <nav className="bg-gray-900 text-white">
      <div className="mx-auto flex h-14 max-w-screen-2xl items-center gap-6 px-6">
        {/* Brand */}
        <a href="/Dashboard" className="shrink-0 font-bold tracking-tight !text-white hover:!text-gray-200 no-underline mr-2">
          Project Ceres
        </a>

        {/* Primary nav */}
        <div className="flex items-center gap-0.5">
          <a href="/Dashboard"    className={navLink}>Dashboard</a>
          <a href="/Movements"    className={navLink}>Movements</a>
          <a href="/Accounts"     className={navLink}>Accounts</a>
          <a href="/Transactions" className={navLink}>Transactions</a>
          <a href="/Transfers"    className={navLink}>Transfers</a>
          <a href="/Categories"   className={navLink}>Categories</a>
          <a href="/Budgets"      className={navLink}>Budgets</a>
          <a href="/Import"       className={navLink}>Import</a>
          <a href="/app/reports"   className={navLink}>Reports</a>
        </div>

        {/* Spacer */}
        <div className="flex-1" />

        {/* Right-side utility links */}
        <div className="flex items-center gap-0.5">

          {/* Review dropdown */}
          <div className="relative" ref={dropdownRef}>
            <button
              onClick={() => setReviewOpen(o => !o)}
              className="inline-flex items-center gap-1.5 !text-gray-300 hover:!text-white hover:bg-gray-800 px-2.5 py-1.5 rounded-md transition-colors text-sm cursor-pointer bg-transparent border-0"
            >
              Reconciliation
              {reviewBadgeCount > 0 && <Badge variant="destructive">{reviewBadgeCount}</Badge>}
              <ChevronDown className={`h-3.5 w-3.5 transition-transform ${reviewOpen ? 'rotate-180' : ''}`} />
            </button>

            {reviewOpen && (
              <div className="absolute right-0 top-full mt-1 w-52 rounded-md bg-gray-800 shadow-lg ring-1 ring-black/20 z-50">
                <a
                  href="/ReconciliationReview"
                  className="flex items-center justify-between px-3 py-2 text-sm !text-gray-300 hover:!text-white hover:bg-gray-700 rounded-t-md no-underline transition-colors"
                  onClick={() => setReviewOpen(false)}
                >
                  Transactions
                  {pendingReconciliations > 0 && <Badge variant="destructive">{pendingReconciliations}</Badge>}
                </a>
                <a
                  href="/TransferReview"
                  className="flex items-center justify-between px-3 py-2 text-sm !text-gray-300 hover:!text-white hover:bg-gray-700 rounded-b-md no-underline transition-colors"
                  onClick={() => setReviewOpen(false)}
                >
                  Transfers
                  {pendingTransfers > 0 && <Badge variant="destructive">{pendingTransfers}</Badge>}
                </a>
              </div>
            )}
          </div>

          <a href="/RecurringTransactions/Upcoming" className={navLinkBadge}>
            <Bell className="h-4 w-4" />
            Reminders
            {upcomingPaymentsCount > 0 && <Badge variant="destructive">{upcomingPaymentsCount}</Badge>}
          </a>
          <a href="/Settings/Edit" className={navLink}>Settings</a>
        </div>
      </div>
    </nav>
  )
}
