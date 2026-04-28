import { Bell } from 'lucide-react'
import { Badge } from '@/components/ui/badge'

interface NavbarProps {
  upcomingPaymentsCount?: number
  pendingTransfers?: number
  pendingReconciliations?: number
}

export function Navbar({ upcomingPaymentsCount = 0, pendingTransfers = 0, pendingReconciliations = 0 }: NavbarProps) {
  return (
    <nav className="bg-gray-900 text-white">
      <div className="mx-auto flex h-14 max-w-screen-xl items-center justify-between px-4">
        <a href="/Dashboard" className="font-bold tracking-tight !text-white hover:!text-gray-200 no-underline">Project Ceres</a>
        <div className="flex items-center gap-1 text-sm">
          <a href="/Dashboard" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Dashboard</a>
          <a href="/Movements" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Movements</a>
          <a href="/Accounts" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Accounts</a>
          <a href="/Transactions" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Transactions</a>
          <a href="/Transfers" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Transfers</a>
          <a href="/Categories" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Categories</a>
          <a href="/Budgets" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Budgets</a>
          <a href="/Import" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Import</a>
          <a href="/TransferReview" className="relative !text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline inline-flex items-center gap-1.5">
            Transfer Review
            {pendingTransfers > 0 && (
              <Badge className="ml-1.5" variant="destructive">
                {pendingTransfers}
              </Badge>
            )}
          </a>
          <a href="/ReconciliationReview" className="relative !text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline inline-flex items-center gap-1.5">
            Reconciliation
            {pendingReconciliations > 0 && (
              <Badge className="ml-1.5" variant="destructive">
                {pendingReconciliations}
              </Badge>
            )}
          </a>
          <a href="/RecurringTransactions/Upcoming" className="relative !text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline inline-flex items-center gap-1.5">
            <Bell className="h-4 w-4" />
            Reminders
            {upcomingPaymentsCount > 0 && (
              <Badge className="ml-1.5" variant="destructive">
                {upcomingPaymentsCount}
              </Badge>
            )}
          </a>
          <a href="/Reports" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Reports</a>
          <a href="/Settings/Edit" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Settings</a>
        </div>
      </div>
    </nav>
  )
}
