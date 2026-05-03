export function buildCsvHref(slug: string, queryString: string): string {
  const qs = queryString ? `${queryString}&format=csv` : 'format=csv';
  return `/api/reports/${slug}?${qs}`;
}
