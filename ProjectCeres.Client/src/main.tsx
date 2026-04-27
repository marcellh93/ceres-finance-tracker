import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { Navbar } from './components/ui/navbar'
import { IsClearedSwitch } from './components/IsClearedSwitch'
import { ClearedBadge } from './components/ClearedBadge'
import { CategoryBudgetBars } from './components/CategoryBudgetBars'
import { GoalBudgetBars } from './components/GoalBudgetBars'
import { NetWorthChart } from './components/NetWorthChart'
import { IncomeExpenseChart } from './components/IncomeExpenseChart'
import { SpendingByCategoryChart } from './components/SpendingByCategoryChart'
import { AccountBalancesChart } from './components/AccountBalancesChart'
import { CashFlowChart } from './components/CashFlowChart'

const navbarEl = document.getElementById('navbar-root')
if (navbarEl) {
  const upcomingCount = parseInt(navbarEl.dataset.upcomingCount ?? '0', 10)
  const pendingTransfers = parseInt(navbarEl.dataset.pendingTransfers ?? '0', 10)
  createRoot(navbarEl).render(
    <StrictMode>
      <Navbar upcomingPaymentsCount={upcomingCount} pendingTransfers={pendingTransfers} />
    </StrictMode>,
  )
}

document.querySelectorAll<HTMLElement>('[data-react="is-cleared-switch"]').forEach((el) => {
  const fieldName = el.dataset.fieldName ?? 'IsCleared'
  const initialChecked = el.dataset.checked === 'true'
  createRoot(el).render(
    <StrictMode>
      <IsClearedSwitch fieldName={fieldName} initialChecked={initialChecked} />
    </StrictMode>,
  )
})

document.querySelectorAll<HTMLElement>('[data-react="cleared-badge"]').forEach((el) => {
  const id = el.dataset.id ?? ''
  const type = (el.dataset.movementType ?? 'transaction') as 'transaction' | 'transfer'
  const isCleared = el.dataset.cleared === 'true'
  const needsReview = el.dataset.needsReview === 'true'
  createRoot(el).render(
    <StrictMode>
      <ClearedBadge id={id} type={type} isCleared={isCleared} needsReview={needsReview} />
    </StrictMode>,
  )
})

const categoryBudgetBarsEl = document.querySelector<HTMLElement>('[data-react="category-budget-bars"]')
if (categoryBudgetBarsEl) {
  createRoot(categoryBudgetBarsEl).render(
    <StrictMode>
      <CategoryBudgetBars />
    </StrictMode>,
  )
}

const goalBudgetBarsEl = document.querySelector<HTMLElement>('[data-react="goal-budget-bars"]')
if (goalBudgetBarsEl) {
  createRoot(goalBudgetBarsEl).render(
    <StrictMode>
      <GoalBudgetBars />
    </StrictMode>,
  )
}

const netWorthChartEl = document.querySelector<HTMLElement>('[data-react="net-worth-chart"]')
if (netWorthChartEl) {
  createRoot(netWorthChartEl).render(
    <StrictMode>
      <NetWorthChart />
    </StrictMode>,
  )
}

const incomeExpenseChartEl = document.querySelector<HTMLElement>('[data-react="income-expense-chart"]')
if (incomeExpenseChartEl) {
  createRoot(incomeExpenseChartEl).render(
    <StrictMode>
      <IncomeExpenseChart />
    </StrictMode>,
  )
}

const spendingByCategoryChartEl = document.querySelector<HTMLElement>('[data-react="spending-by-category-chart"]')
if (spendingByCategoryChartEl) {
  createRoot(spendingByCategoryChartEl).render(
    <StrictMode>
      <SpendingByCategoryChart />
    </StrictMode>,
  )
}

const accountBalancesChartEl = document.querySelector<HTMLElement>('[data-react="account-balances-chart"]')
if (accountBalancesChartEl) {
  createRoot(accountBalancesChartEl).render(
    <StrictMode>
      <AccountBalancesChart />
    </StrictMode>,
  )
}

const cashFlowChartEl = document.querySelector<HTMLElement>('[data-react="cash-flow-chart"]')
if (cashFlowChartEl) {
  createRoot(cashFlowChartEl).render(
    <StrictMode>
      <CashFlowChart />
    </StrictMode>,
  )
}
