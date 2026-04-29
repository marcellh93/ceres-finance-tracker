import { fireEvent, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useKeyboardShortcut } from './use-keyboard-shortcut';

function Probe({
  shortcut,
  callback,
}: {
  shortcut: string;
  callback: () => void;
}) {
  useKeyboardShortcut(shortcut, callback);
  return (
    <>
      <input data-testid="text-input" />
      <textarea data-testid="textarea" />
      <button data-testid="button">click</button>
    </>
  );
}

describe('useKeyboardShortcut', () => {
  let originalUserAgent: string;

  beforeEach(() => {
    originalUserAgent = navigator.userAgent;
  });

  afterEach(() => {
    Object.defineProperty(navigator, 'userAgent', {
      value: originalUserAgent,
      configurable: true,
    });
  });

  function setUserAgent(ua: string) {
    Object.defineProperty(navigator, 'userAgent', { value: ua, configurable: true });
  }

  it('fires on Cmd+K on Mac', () => {
    setUserAgent('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)');
    const cb = vi.fn();
    render(<Probe shortcut="mod+k" callback={cb} />);
    fireEvent.keyDown(window, { key: 'k', metaKey: true });
    expect(cb).toHaveBeenCalledTimes(1);
  });

  it('does NOT fire on Ctrl+K on Mac', () => {
    setUserAgent('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)');
    const cb = vi.fn();
    render(<Probe shortcut="mod+k" callback={cb} />);
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
    expect(cb).not.toHaveBeenCalled();
  });

  it('fires on Ctrl+K on non-Mac', () => {
    setUserAgent('Mozilla/5.0 (X11; Linux x86_64)');
    const cb = vi.fn();
    render(<Probe shortcut="mod+k" callback={cb} />);
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
    expect(cb).toHaveBeenCalledTimes(1);
  });

  it('does NOT fire when an <input> is focused', () => {
    setUserAgent('Mozilla/5.0 (X11; Linux x86_64)');
    const cb = vi.fn();
    const { getByTestId } = render(<Probe shortcut="mod+k" callback={cb} />);
    const input = getByTestId('text-input') as HTMLInputElement;
    input.focus();
    fireEvent.keyDown(input, { key: 'k', ctrlKey: true });
    expect(cb).not.toHaveBeenCalled();
  });

  it('does NOT fire when a <textarea> is focused', () => {
    setUserAgent('Mozilla/5.0 (X11; Linux x86_64)');
    const cb = vi.fn();
    const { getByTestId } = render(<Probe shortcut="mod+k" callback={cb} />);
    const ta = getByTestId('textarea') as HTMLTextAreaElement;
    ta.focus();
    fireEvent.keyDown(ta, { key: 'k', ctrlKey: true });
    expect(cb).not.toHaveBeenCalled();
  });

  it('cleans up the listener on unmount', () => {
    setUserAgent('Mozilla/5.0 (X11; Linux x86_64)');
    const cb = vi.fn();
    const { unmount } = render(<Probe shortcut="mod+k" callback={cb} />);
    unmount();
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
    expect(cb).not.toHaveBeenCalled();
  });
});
