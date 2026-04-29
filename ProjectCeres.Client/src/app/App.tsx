import { Route, Routes } from 'react-router-dom';
import { AppLayout } from './layout/AppLayout';
import { Accounts } from './pages/Accounts';
import { Budgets } from './pages/Budgets';
import { Categories } from './pages/Categories';
import { Dashboard } from './pages/Dashboard';
import { Import } from './pages/Import';
import { Movements } from './pages/Movements';
import { NotFound } from './pages/NotFound';
import { Profile } from './pages/Profile';
import { Recurring } from './pages/Recurring';
import { Reports } from './pages/Reports';
import { Review } from './pages/Review';
import { Security } from './pages/Security';
import { Settings } from './pages/Settings';
import { Support } from './pages/Support';
import { Transactions } from './pages/Transactions';
import { Transfers } from './pages/Transfers';

export function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="movements" element={<Movements />} />
        <Route path="transactions" element={<Transactions />} />
        <Route path="transfers" element={<Transfers />} />
        <Route path="review" element={<Review />} />
        <Route path="accounts" element={<Accounts />} />
        <Route path="categories" element={<Categories />} />
        <Route path="budgets" element={<Budgets />} />
        <Route path="recurring" element={<Recurring />} />
        <Route path="import" element={<Import />} />
        <Route path="reports" element={<Reports />} />
        <Route path="settings" element={<Settings />} />
        <Route path="support" element={<Support />} />
        <Route path="profile" element={<Profile />} />
        <Route path="security" element={<Security />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}
