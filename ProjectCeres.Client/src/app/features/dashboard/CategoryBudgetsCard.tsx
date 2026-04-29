import { Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { CategoryBudgetBars } from '@/components/CategoryBudgetBars';

export function CategoryBudgetsCard() {
  return (
    <Card>
      <CardHeader className="flex flex-row items-baseline justify-between">
        <CardTitle>Category Budgets</CardTitle>
        <Link to="/budgets/categories" className="text-sm text-muted-foreground hover:text-foreground">
          View all →
        </Link>
      </CardHeader>
      <CardContent>
        <CategoryBudgetBars />
      </CardContent>
    </Card>
  );
}
