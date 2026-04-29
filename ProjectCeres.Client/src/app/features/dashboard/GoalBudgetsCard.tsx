import { Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { GoalBudgetBars } from '@/components/GoalBudgetBars';

export function GoalBudgetsCard() {
  return (
    <Card>
      <CardHeader className="flex flex-row items-baseline justify-between">
        <CardTitle>Goal Budgets</CardTitle>
        <Link to="/budgets/goals" className="text-sm text-muted-foreground hover:text-foreground">
          View all →
        </Link>
      </CardHeader>
      <CardContent>
        <GoalBudgetBars />
      </CardContent>
    </Card>
  );
}
