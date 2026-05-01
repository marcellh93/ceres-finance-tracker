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

export type NavItem = {
  /** Path under /app, including the leading slash. */
  to: string;
  /** Visible label and aria-label source. */
  label: string;
  /** Lucide icon component. */
  icon: LucideIcon;
};

export type NavGroup = {
  /** Group heading shown in expanded mode. */
  label: string;
  items: NavItem[];
};

export const navGroups: NavGroup[] = [
  {
    label: 'Activity',
    items: [
      { to: '/movements', label: 'Movements', icon: LayoutList },
      { to: '/review',    label: 'Review',    icon: Inbox },
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
