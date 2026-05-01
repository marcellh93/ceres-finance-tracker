import { Link } from 'react-router-dom';
import { CheckCircle2 } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { useApi } from '../../lib/use-api';
import { CardError } from '../../components/CardError';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { formatDate } from '../../lib/date-format';
import { useSettings } from '../../lib/use-settings';

const PREVIEW_LIMIT = 5;
const UPCOMING_URL = `/api/dashboard/reminders/upcoming?limit=${PREVIEW_LIMIT}&days=7`;

type UpcomingReminderDto = {
  id: string;
  name: string;
  nextDueDate: string; // yyyy-MM-dd
  estimatedAmount: number | null;
  currencySymbol: string;
  accountName: string;
};

type UpcomingResponse = {
  totalCount: number;
  items: UpcomingReminderDto[];
};

export function RemindersCard() {
  const { data, error, loading, refetch } = useApi<UpcomingResponse>(UPCOMING_URL);
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';
  const dateFormat = settings.data?.dateFormat;

  const total = data?.totalCount ?? 0;
  const items = data?.items ?? [];
  const overflow = total - items.length;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Reminders</CardTitle>
        <p className="text-xs text-muted-foreground">Next 7 days</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-24 w-full" />}
        {error && <CardError section="Reminders" onRetry={refetch} />}
        {data && total === 0 && (
          <div className="flex flex-col items-center justify-center gap-3 min-h-[120px]">
            <CheckCircle2 className="h-12 w-12 text-success" aria-hidden="true" />
            <p className="text-base font-medium text-foreground">All caught up</p>
          </div>
        )}
        {data && items.length > 0 && (
          <ul className="divide-y">
            {items.map((r) => (
              <li
                key={r.id}
                className="flex items-center justify-between gap-3 py-2 text-sm first:pt-0 last:pb-0"
              >
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium">{r.name}</div>
                  <div className="text-xs text-muted-foreground">
                    {formatDate(r.nextDueDate, dateFormat)} · {r.accountName}
                  </div>
                </div>
                {r.estimatedAmount != null && (
                  <Numeric className="whitespace-nowrap text-sm">
                    {r.currencySymbol} {formatNumberForDisplay(r.estimatedAmount, numberFormat)}
                  </Numeric>
                )}
              </li>
            ))}
            <li className="flex items-center justify-between pt-2 text-xs text-muted-foreground">
              {overflow > 0 ? <span>+{overflow} more</span> : <span />}
              <Link to="/recurring" className="hover:text-foreground">
                View all →
              </Link>
            </li>
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
