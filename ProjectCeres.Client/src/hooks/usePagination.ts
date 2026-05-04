import { useState, useMemo } from 'react';

export function usePagination<T>(items: T[], pageSize: number) {
  const [currentPage, setCurrentPage] = useState(1);

  const totalPages = useMemo(
    () => Math.max(1, Math.ceil(items.length / pageSize)),
    [items.length, pageSize],
  );

  const paginatedItems = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return items.slice(start, start + pageSize);
  }, [items, currentPage, pageSize]);

  function next() {
    setCurrentPage((p) => Math.min(p + 1, totalPages));
  }

  function prev() {
    setCurrentPage((p) => Math.max(p - 1, 1));
  }

  function goTo(page: number) {
    setCurrentPage(Math.max(1, Math.min(page, totalPages)));
  }

  return { paginatedItems, currentPage, totalPages, next, prev, goTo };
}
