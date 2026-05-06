export function formatK(v: number): string {
  if (v === 0) return '0';
  const k = v / 1000;
  return `${k.toFixed(Math.abs(k) < 10 ? 1 : 0).replace(/\.0$/, '')}k`;
}

export function truncateLabel(value: string, max = 18): string {
  return value.length > max ? `${value.slice(0, max - 1)}…` : value;
}
