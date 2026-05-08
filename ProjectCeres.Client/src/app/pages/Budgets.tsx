import { BudgetsLayout } from '../features/budgets/BudgetsLayout';
import { useDocumentTitle } from '../lib/use-document-title';

export function Budgets() {
  useDocumentTitle('Budgets');
  return <BudgetsLayout />;
}
