import { describe, it, expect } from 'vitest';
import { classifyStatus, type RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: 'abc', name: 'Test', estimatedAmount: 100, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Bills', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: TODAY,
    isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    ...overrides,
  };
}

describe('classifyStatus', () => {
  it('archived reminder returns [archived] only', () => {
    expect(classifyStatus(makeReminder({ isActive: false, nextDueDate: '2026-04-01' }), TODAY))
      .toEqual(['archived']);
  });

  it('overdue returns [overdue]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-04-30' }), TODAY))
      .toEqual(['overdue']);
  });

  it('due today returns [dueToday]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: TODAY }), TODAY))
      .toEqual(['dueToday']);
  });

  it('upcoming returns []', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-05-10' }), TODAY))
      .toEqual([]);
  });

  it('overdue ManualDate returns [overdue, manual]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-04-01', reminderBehaviour: 'ManualDate' }), TODAY))
      .toEqual(['overdue', 'manual']);
  });

  it('upcoming ManualDate returns [manual]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-06-01', reminderBehaviour: 'ManualDate' }), TODAY))
      .toEqual(['manual']);
  });
});
