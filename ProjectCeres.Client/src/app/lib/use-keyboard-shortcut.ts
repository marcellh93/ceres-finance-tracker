import { useEffect } from 'react';

function isMac(): boolean {
  return /Mac|iPhone|iPad/.test(navigator.userAgent);
}

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    target.isContentEditable
  );
}

/**
 * Bind a keyboard shortcut to a callback for the lifetime of the calling
 * component. Currently supports the `mod+<key>` form, where `mod` resolves
 * to Cmd on Mac and Ctrl elsewhere. The shortcut is suppressed when an
 * editable element has focus.
 */
export function useKeyboardShortcut(shortcut: string, callback: () => void): void {
  useEffect(() => {
    const [modifier, key] = shortcut.split('+');
    if (modifier !== 'mod' || !key) {
      throw new Error(`Unsupported shortcut format: "${shortcut}". Expected "mod+<key>".`);
    }
    const expectedKey = key.toLowerCase();

    const handler = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== expectedKey) return;
      if (isEditableTarget(event.target)) return;
      const modPressed = isMac() ? event.metaKey : event.ctrlKey;
      const wrongModPressed = isMac() ? event.ctrlKey : event.metaKey;
      if (!modPressed || wrongModPressed) return;
      event.preventDefault();
      callback();
    };

    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [shortcut, callback]);
}
