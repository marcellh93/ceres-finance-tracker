export function buildTypeFilterParams(prev: URLSearchParams, value: string | null): URLSearchParams {
  const next = new URLSearchParams(prev);
  if (value) next.set('type', value);
  else next.delete('type');
  next.delete('page');
  return next;
}
