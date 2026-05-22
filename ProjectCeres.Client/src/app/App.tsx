import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { Toaster } from '@/components/ui/sonner';
import { AppLayout } from './layout/AppLayout';
import { AuthLayout } from './layout/AuthLayout';
import { RequireAuth } from './auth/RequireAuth';
import { Accounts } from './pages/Accounts';
import { Budgets } from './pages/Budgets';
import { Categories } from './pages/Categories';
import { Dashboard } from './pages/Dashboard';
import { Import } from './pages/Import';
import { NotFound } from './pages/NotFound';
import { Profile } from './pages/Profile';
import { Recurring } from './pages/Recurring';
import { ReportsLayout } from './features/reports/ReportsLayout';
import { NetWorth } from './features/reports/NetWorth';
import { NetWorthOverTime } from './features/reports/NetWorthOverTime';
import { IncomeExpense } from './features/reports/IncomeExpense';
import { MonthlyCashFlow } from './features/reports/MonthlyCashFlow';
import { ExpenseBreakdown } from './features/reports/ExpenseBreakdown';
import { BudgetVsActual } from './features/reports/BudgetVsActual';
import { LargestExpenses } from './features/reports/LargestExpenses';
import { TransactionHistory } from './features/reports/TransactionHistory';
import { Review } from './pages/Review';
// Security lazy-loaded: TOTP wizard pulls qrcode.react + the full security
// feature folder, only needed when the user visits /app/security. Keeps the
// main app chunk thin (Phase 2 budget breach 2026-05-18).
const Security = lazy(() => import('./pages/Security').then((m) => ({ default: m.Security })));
import { Settings } from './pages/Settings';
import { Support } from './pages/Support';
import { AccountCreate } from './features/accounts/AccountCreate';
import { AccountEdit } from './features/accounts/AccountEdit';
import { AccountLedger } from './features/accounts/AccountLedger';
import { BudgetCreate } from './features/budgets/BudgetCreate';
import { BudgetEdit } from './features/budgets/BudgetEdit';
import { CategoryCreate } from './features/categories/CategoryCreate';
import { CategoryEdit } from './features/categories/CategoryEdit';
import { ProfilesLayout } from './features/import/ProfilesLayout';
import { ProfileCreate } from './features/import/ProfileCreate';
import { ProfileEdit } from './features/import/ProfileEdit';
import { MovementCreate } from './features/movements/MovementCreate';
import { MovementEdit } from './features/movements/MovementEdit';
import { MovementsLayout } from './features/movements/MovementsLayout';
import { RecurringCreate } from './features/recurring/RecurringCreate';
import { RecurringEdit } from './features/recurring/RecurringEdit';
import { useRecurringLayoutCtx } from './features/recurring/RecurringLayout';
import { Login } from './pages/auth/Login';
import { LoginTotp } from './pages/auth/LoginTotp';
import { PasswordReset } from './pages/auth/PasswordReset';
import { Register } from './pages/auth/Register';

function RecurringCreateBridge() {
  const ctx = useRecurringLayoutCtx();
  return <RecurringCreate ctx={ctx} />;
}

function RecurringEditBridge() {
  const ctx = useRecurringLayoutCtx();
  return <RecurringEdit ctx={ctx} />;
}

export function App() {
  return (
    <>
      {/*
        Single root-level Toaster. Pre-fix, <Toaster> was mounted only inside
        AppLayout — toasts fired from auth pages (which use AuthLayout) silently
        no-op'd. Surfaced 2026-05-18 when the `?expired=1` toast on /login
        didn't render. Mounting once at the App root lets BOTH layouts inherit
        sonner without duplicating the component or racing on navigation.
      */}
      <Toaster />
    <Routes>
      {/* Public branch — auth pages with the centered-card layout, no app shell. */}
      <Route element={<AuthLayout />}>
        <Route path="login" element={<Login />} />
        <Route path="login/totp" element={<LoginTotp />} />
        <Route path="password-reset" element={<PasswordReset />} />
        <Route path="register" element={<Register />} />
      </Route>

      {/* Protected branch — everything that exists today, gated by RequireAuth. */}
      <Route
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        <Route index element={<Dashboard />} />
        <Route path="movements" element={<MovementsLayout />}>
          <Route path="new" element={<MovementCreate />} />
          <Route path=":id/edit" element={<MovementEdit />} />
        </Route>
        <Route path="review" element={<Review />} />
        <Route path="accounts" element={<Accounts />}>
          <Route path="new" element={<AccountCreate />} />
          <Route path=":id/edit" element={<AccountEdit />} />
        </Route>
        <Route path="accounts/:id/ledger" element={<AccountLedger />} />
        <Route path="categories" element={<Categories />}>
          <Route path="new" element={<CategoryCreate />} />
          <Route path=":id/edit" element={<CategoryEdit />} />
        </Route>
        <Route path="budgets" element={<Budgets />}>
          <Route path="new" element={<BudgetCreate />} />
          <Route path=":id/edit" element={<BudgetEdit />} />
        </Route>
        <Route path="recurring" element={<Recurring />}>
          <Route path="new" element={<RecurringCreateBridge />} />
          <Route path=":id/edit" element={<RecurringEditBridge />} />
        </Route>
        <Route path="import" element={<Import />} />
        <Route path="import/profiles" element={<ProfilesLayout />}>
          <Route path="new" element={<ProfileCreate />} />
          <Route path=":id/edit" element={<ProfileEdit />} />
        </Route>
        <Route path="reports" element={<ReportsLayout />}>
          <Route index element={<Navigate to="net-worth" replace />} />
          <Route path="net-worth" element={<NetWorth />} />
          <Route path="net-worth-over-time" element={<NetWorthOverTime />} />
          <Route path="income-expense" element={<IncomeExpense />} />
          <Route path="monthly-cash-flow" element={<MonthlyCashFlow />} />
          <Route path="expense-breakdown" element={<ExpenseBreakdown />} />
          <Route path="budget-vs-actual" element={<BudgetVsActual />} />
          <Route path="largest-expenses" element={<LargestExpenses />} />
          <Route path="transaction-history" element={<TransactionHistory />} />
        </Route>
        <Route path="settings" element={<Settings />} />
        <Route path="support" element={<Support />} />
        <Route path="profile" element={<Profile />} />
        <Route
          path="security"
          element={
            <Suspense fallback={null}>
              <Security />
            </Suspense>
          }
        />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
    </>
  );
}
