type Cb = () => void;
const listeners = new Set<Cb>();

export function openConsentManager() {
  listeners.forEach((cb) => cb());
}

export function onOpenConsentManager(cb: Cb) {
  listeners.add(cb);
  return () => listeners.delete(cb);
}
