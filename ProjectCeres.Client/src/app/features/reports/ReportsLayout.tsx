import { Outlet } from 'react-router-dom';
import { ReportsTabBar } from './ReportsTabBar';
import { ReportsSharedFilterBar } from './ReportsSharedFilterBar';

export function ReportsLayout() {
  return (
    <div className="flex flex-col">
      <ReportsTabBar />
      <ReportsSharedFilterBar />
      <div className="px-[8%] py-6">
        <Outlet />
      </div>
    </div>
  );
}
