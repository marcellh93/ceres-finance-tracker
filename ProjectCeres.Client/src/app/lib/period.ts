const pad = (n: number) => String(n).padStart(2, '0');
const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

function applyStartDayInMonth(year: number, month: number, startDay: number): Date {
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  return new Date(year, month, Math.min(startDay, daysInMonth));
}

/**
 * Returns the JS-zero-indexed {year, month} of the current budget period.
 * Mirrors BudgetPeriod.GetCurrentPeriodMonth from the server.
 * month is 0-indexed (Jan=0) to match JS Date conventions.
 */
export function getCurrentPeriodMonth(today: Date, startDay: number): { year: number; month: number } {
  if (startDay < 1 || startDay > 31) throw new RangeError('startDay must be 1–31');
  if (startDay === 1) return { year: today.getFullYear(), month: today.getMonth() };
  const thisMonthStart = applyStartDayInMonth(today.getFullYear(), today.getMonth(), startDay);
  if (today < thisMonthStart) return { year: today.getFullYear(), month: today.getMonth() };
  const next = new Date(today.getFullYear(), today.getMonth() + 1, 1);
  return { year: next.getFullYear(), month: next.getMonth() };
}

/**
 * Returns ISO yyyy-MM-dd {from, to} bounds for the budget period identified by
 * (year, month) — JS zero-indexed. Mirrors BudgetPeriod.GetBoundsForMonth.
 */
export function getBoundsForMonth(year: number, month: number, startDay: number): { from: string; to: string } {
  if (startDay === 1) {
    const last = new Date(year, month + 1, 0).getDate();
    return { from: `${year}-${pad(month + 1)}-01`, to: `${year}-${pad(month + 1)}-${pad(last)}` };
  }
  const thisStart = applyStartDayInMonth(year, month, startDay);
  const end = new Date(thisStart);
  end.setDate(end.getDate() - 1);
  const prevMonth = month === 0 ? 11 : month - 1;
  const prevYear = month === 0 ? year - 1 : year;
  const start = applyStartDayInMonth(prevYear, prevMonth, startDay);
  return { from: iso(start), to: iso(end) };
}
