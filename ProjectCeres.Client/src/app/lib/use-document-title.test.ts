import { renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useDocumentTitle } from './use-document-title';

describe('useDocumentTitle', () => {
  let originalTitle: string;

  beforeEach(() => {
    originalTitle = document.title;
    document.title = 'Original';
  });

  afterEach(() => {
    document.title = originalTitle;
  });

  it('sets the document title to "<page> — Project Ceres" on mount', () => {
    renderHook(() => useDocumentTitle('Movements'));
    expect(document.title).toBe('Movements — Project Ceres');
  });

  it('restores the previous title on unmount', () => {
    document.title = 'Before';
    const { unmount } = renderHook(() => useDocumentTitle('Movements'));
    expect(document.title).toBe('Movements — Project Ceres');
    unmount();
    expect(document.title).toBe('Before');
  });

  it('updates the title when the page prop changes', () => {
    const { rerender } = renderHook(({ page }) => useDocumentTitle(page), {
      initialProps: { page: 'Movements' },
    });
    expect(document.title).toBe('Movements — Project Ceres');
    rerender({ page: 'Reports' });
    expect(document.title).toBe('Reports — Project Ceres');
  });
});
