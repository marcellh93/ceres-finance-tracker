import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { readCollapsed, SIDEBAR_STORAGE_KEY, writeCollapsed } from './sidebar-storage';

describe('sidebar-storage', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('writeCollapsed then readCollapsed round-trips', () => {
    writeCollapsed(true);
    expect(readCollapsed()).toBe(true);
    writeCollapsed(false);
    expect(readCollapsed()).toBe(false);
  });

  it('writes the literal string "true" or "false" under the documented key', () => {
    writeCollapsed(true);
    expect(localStorage.getItem(SIDEBAR_STORAGE_KEY)).toBe('true');
    writeCollapsed(false);
    expect(localStorage.getItem(SIDEBAR_STORAGE_KEY)).toBe('false');
  });

  it('readCollapsed returns false when storage is empty', () => {
    expect(readCollapsed()).toBe(false);
  });

  it('readCollapsed returns false when storage holds a malformed value', () => {
    localStorage.setItem(SIDEBAR_STORAGE_KEY, 'maybe');
    expect(readCollapsed()).toBe(false);
  });

  it('readCollapsed returns false when localStorage.getItem throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('private mode');
    });
    expect(readCollapsed()).toBe(false);
  });

  it('writeCollapsed swallows errors when localStorage.setItem throws', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('private mode');
    });
    expect(() => writeCollapsed(true)).not.toThrow();
  });
});
