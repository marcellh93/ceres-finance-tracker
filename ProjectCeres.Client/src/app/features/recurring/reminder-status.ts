export type ReminderStatus = 'overdue' | 'dueToday' | 'upcoming' | 'archived' | 'manual';

export type RecurringTransactionListItemDto = {
  id: string;
  name: string;
  estimatedAmount: number | null;
  accountId: string;
  accountName: string;
  currencySymbol: string;
  categoryId: string;
  categoryName: string;
  categoryTypeName: string;
  frequency: string;
  dayOfPeriod: number | null;
  nextDueDate: string;
  isActive: boolean;
  reminderBehaviour: string;
};

export function classifyStatus(
  reminder: RecurringTransactionListItemDto,
  today: string
): ReminderStatus[] {
  if (!reminder.isActive) return ['archived'];
  const out: ReminderStatus[] = [];
  if (reminder.nextDueDate < today) out.push('overdue');
  else if (reminder.nextDueDate === today) out.push('dueToday');
  if (reminder.reminderBehaviour === 'ManualDate') out.push('manual');
  return out;
}
