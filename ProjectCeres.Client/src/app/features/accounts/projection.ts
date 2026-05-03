export type ProjectionResult =
  | {
      ok: true;
      monthsToPayoff: number;
      totalPaid: number;
      totalInterest: number;
      payoffDate: Date;
    }
  | { ok: false; error: string };

export function project(
  balance: number,
  annualRate: number,
  monthlyPayment: number,
): ProjectionResult {
  if (balance <= 0) {
    return { ok: false, error: 'Outstanding balance must be greater than zero.' };
  }
  if (monthlyPayment <= 0) {
    return { ok: false, error: 'Monthly payment must be greater than zero.' };
  }

  if (annualRate <= 0) {
    const monthsToPayoff = Math.ceil(balance / monthlyPayment);
    const totalPaid = balance;
    const payoffDate = addMonths(new Date(), monthsToPayoff);
    return { ok: true, monthsToPayoff, totalPaid, totalInterest: 0, payoffDate };
  }

  const monthlyRate = annualRate / 12;
  const monthlyInterestCost = balance * monthlyRate;
  if (monthlyPayment <= monthlyInterestCost) {
    return {
      ok: false,
      error:
        'Monthly payment is too low to cover the monthly interest. Balance would never be paid off.',
    };
  }

  const monthsToPayoff = Math.ceil(
    -Math.log(1 - (balance * monthlyRate) / monthlyPayment) / Math.log(1 + monthlyRate),
  );
  const totalPaid = monthsToPayoff * monthlyPayment;
  const totalInterest = totalPaid - balance;
  const payoffDate = addMonths(new Date(), monthsToPayoff);

  return { ok: true, monthsToPayoff, totalPaid, totalInterest, payoffDate };
}

function addMonths(d: Date, months: number): Date {
  const r = new Date(d);
  r.setMonth(r.getMonth() + months);
  return r;
}
