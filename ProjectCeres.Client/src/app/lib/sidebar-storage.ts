export const SIDEBAR_STORAGE_KEY = 'ceres.sidebar.collapsed';

export function readCollapsed(): boolean {
  try {
    return localStorage.getItem(SIDEBAR_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

export function writeCollapsed(collapsed: boolean): void {
  try {
    localStorage.setItem(SIDEBAR_STORAGE_KEY, collapsed ? 'true' : 'false');
  } catch {
    // Private mode or quota exceeded — collapse state is non-critical, swallow.
  }
}
