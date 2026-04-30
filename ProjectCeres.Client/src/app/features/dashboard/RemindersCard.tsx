import { Link } from 'react-router-dom';
import { CheckCircle2 } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { CardError } from '../../components/CardError';
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
          <div className="flex flex-col items-center justify-center gap-3 min-h-[120px]">
            <CheckCircle2 className="h-12 w-12 text-success" aria-hidden="true" />
            <p className="text-base font-medium text-foreground">All caught up</p>
          </div>
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
