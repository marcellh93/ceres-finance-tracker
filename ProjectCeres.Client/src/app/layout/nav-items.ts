import {
  BarChart3,
  Inbox,
  Landmark,
  LayoutList,
  LifeBuoy,
  Repeat,
  Settings as SettingsIcon,
  Tags,
  Upload,
  Wallet,
  type LucideIcon,
} from 'lucide-react';
import { useReviewCount } from '../features/review/ReviewCountProvider';

/** Hook returning a numeric badge count to display next to a nav item. */
export type NavBadgeHook = () => number;

export type NavItem = {
  /** Path under /app, including the leading slash. */
  to: string;
  /** Visible label and aria-label source. */
  label: string;
  /** Lucide icon component. */
  icon: LucideIcon;
  /** Optional hook returning a badge count. Pill renders only when > 0. */
  useBadge?: NavBadgeHook;
};

export type NavGroup = {
  /** Group heading shown in expanded mode. */
  label: string;
  items: NavItem[];
};

const useReviewBadge: NavBadgeHook = () => useReviewCount().total;

export const navGroups: NavGroup[] = [
  {
    label: 'Activity',
    items: [
      { to: '/movements', label: 'Movements', icon: LayoutList },
      { to: '/review',    label: 'Review',    icon: Inbox, useBadge: useReviewBadge },
    ],
  },
  {
    label: 'Money',
    items: [
      { to: '/accounts',   label: 'Accounts',   icon: Landmark },
      { to: '/categories', label: 'Categories', icon: Tags },
      { to: '/budgets',    label: 'Budgets',    icon: Wallet },
    ],
  },
  {
    label: 'Tools',
    items: [
      { to: '/recurring', label: 'Recurring Transactions', icon: Repeat },
      { to: '/import',    label: 'Import',                  icon: Upload },
      { to: '/reports',   label: 'Reports',                 icon: BarChart3 },
    ],
  },
];

export const bottomItems: NavItem[] = [
  { to: '/settings', label: 'Settings', icon: SettingsIcon },
  { to: '/support',  label: 'Support',  icon: LifeBuoy },
];
