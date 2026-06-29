import { useState, useMemo } from 'react';

export function usePagination<T>(items: T[], pageSize: number) {
  const [requestedPage, setRequestedPage] = useState(1);

  const totalPages = useMemo(
    () => Math.max(1, Math.ceil(items.length / pageSize)),
    [items.length, pageSize],
  );

  // Derive the effective page during render — clamps requestedPage against
  // totalPages without needing an effect. This is the React docs' "derive
  // during render" fix for the set-state-in-effect anti-pattern.
  const currentPage = Math.min(requestedPage, totalPages);

  const paginatedItems = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return items.slice(start, start + pageSize);
  }, [items, currentPage, pageSize]);

  function next() {
    setRequestedPage((p) => Math.min(p + 1, totalPages));
  }

  function prev() {
    setRequestedPage((p) => Math.max(p - 1, 1));
  }

  function goTo(page: number) {
    setRequestedPage(() => {
      const pages = Math.max(1, Math.ceil(items.length / pageSize));
      return Math.max(1, Math.min(page, pages));
    });
  }

  return { paginatedItems, currentPage, totalPages, next, prev, goTo };
}
