import { Link } from 'react-router-dom';
import { CheckCircle2 } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { SUMMARY_URL, type SummaryDto } from './api';

export function RemindersCard() {
  const { data, error, loading, refetch } = useApi<SummaryDto>(SUMMARY_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Reminders</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-6 w-32" />}
        {error && <CardError section="Reminders" onRetry={refetch} />}
        {data && data.remindersDueCount === 0 && (
          <p className="flex items-center gap-2 text-sm text-foreground">
            <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
            All caught up
          </p>
        )}
        {data && data.remindersDueCount > 0 && (
          <p className="text-sm">
            <strong className="font-semibold">{data.remindersDueCount}</strong>{' '}
            {data.remindersDueCount === 1 ? 'reminder' : 'reminders'} due.{' '}
            <Link to="/recurring" className="text-muted-foreground hover:text-foreground">
              View all →
            </Link>
          </p>
        )}
      </CardContent>
    </Card>
  );
}
