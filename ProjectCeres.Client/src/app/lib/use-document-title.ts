import { useEffect } from 'react';

/**
 * Sets `document.title` to `${page} — Project Ceres` while the calling
 * component is mounted, and restores the previous title on unmount.
 *
 * Use once per top-level page component. Nested layouts can also call it,
 * but the deepest active hook wins because effects run leaf-first.
 */
export function useDocumentTitle(page: string) {
  useEffect(() => {
    const previous = document.title;
    document.title = `${page} — Project Ceres`;
    return () => {
      document.title = previous;
    };
  }, [page]);
}
