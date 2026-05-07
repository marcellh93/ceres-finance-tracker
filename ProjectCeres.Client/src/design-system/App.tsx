import { NavLink, Route, Routes } from 'react-router-dom';
import { ThemeToggle } from './components/ThemeToggle';
import { Overview } from './pages/Overview';
import { Colors } from './pages/Colors';
import { Typography } from './pages/Typography';
import { Spacing } from './pages/Spacing';
import { Motion } from './pages/Motion';
import { Charts } from './pages/Charts';
import { Toasts } from './pages/Toasts';
import { Patterns } from './pages/Patterns';
import { Components } from './pages/Components';

const sections = [
  { to: '/', label: 'Overview', end: true },
  { to: '/colors', label: 'Colors' },
  { to: '/typography', label: 'Typography' },
  { to: '/spacing', label: 'Spacing, Radius & Shadow' },
  { to: '/motion', label: 'Motion' },
  { to: '/charts', label: 'Charts' },
  { to: '/toasts', label: 'Toasts' },
  { to: '/patterns', label: 'Patterns' },
  { to: '/components', label: 'Components' },
];

export function App() {
  return (
    <div className="grid h-screen grid-cols-[240px_1fr] bg-background text-foreground">
      <aside className="border-r border-border p-6">
        <header className="mb-6 flex items-center justify-between">
          <h1 className="text-lg font-semibold">Ceres DS</h1>
          <ThemeToggle showLabel />
        </header>
        <nav className="flex flex-col gap-1">
          {sections.map((s) => (
            <NavLink
              key={s.to}
              to={s.to}
              end={s.end}
              className={({ isActive }) =>
                [
                  'rounded-md px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-accent text-accent-foreground'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                ].join(' ')
              }
            >
              {s.label}
            </NavLink>
          ))}
        </nav>
      </aside>
      <main className="overflow-y-auto p-10">
        <Routes>
          <Route path="/" element={<Overview />} />
          <Route path="/colors" element={<Colors />} />
          <Route path="/typography" element={<Typography />} />
          <Route path="/spacing" element={<Spacing />} />
          <Route path="/motion" element={<Motion />} />
          <Route path="/charts" element={<Charts />} />
          <Route path="/toasts" element={<Toasts />} />
          <Route path="/patterns" element={<Patterns />} />
          <Route path="/components" element={<Components />} />
        </Routes>
      </main>
    </div>
  );
}
