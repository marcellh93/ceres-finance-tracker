import { Outlet } from 'react-router-dom';
import { ReportsFilterBar } from './ReportsFilterBar';

export function ReportsLayout() {
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <ReportsFilterBar />
      <Outlet />
    </div>
  );
}
