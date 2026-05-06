import { Link } from 'react-router-dom';
import {
  AlertTriangle,
  Check,
  CircleAlert,
  ListChecks,
  RefreshCcw,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { SaveProfilePrompt } from './SaveProfilePrompt';
import type { ImportColumnMappings, ImportFormat, ImportResult } from './import-api';

type Props = {
  result: ImportResult;
  /** When non-null, the user picked a saved profile — don't offer Save-as-profile. */
  selectedProfileId: string | null;
  fileFormat: ImportFormat;
  mappings: ImportColumnMappings;
  onImportAnother: () => void;
};

type Tone = 'success' | 'info' | 'warning' | 'danger' | 'neutral';

const TONE: Record<Tone, string> = {
  success: 'border-emerald-200 bg-emerald-50 dark:border-emerald-900/40 dark:bg-emerald-950/30',
  info:    'border-sky-200 bg-sky-50 dark:border-sky-900/40 dark:bg-sky-950/30',
  warning: 'border-amber-200 bg-amber-50 dark:border-amber-900/40 dark:bg-amber-950/30',
  danger:  'border-destructive/30 bg-destructive/10',
  neutral: 'border-border bg-muted/40',
};

export function StepResult({
  result,
  selectedProfileId,
  fileFormat,
  mappings,
  onImportAnother,
}: Props) {
  return (
    <div className="space-y-6">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
        <Tile
          tone="success"
          icon={<Check className="h-5 w-5 text-emerald-700 dark:text-emerald-300" />}
          label="Imported"
          value={result.rowsImported}
        />
        <Tile
          tone="info"
          icon={<RefreshCcw className="h-5 w-5 text-sky-700 dark:text-sky-300" />}
          label="Reconciled"
          value={result.rowsReconciled}
          to={result.rowsReconciled > 0 ? '/review?tab=reconciliations' : undefined}
        />
        <Tile
          tone="warning"
          icon={<AlertTriangle className="h-5 w-5 text-amber-700 dark:text-amber-300" />}
          label="Needs review"
          value={result.rowsFlagged}
          to={result.rowsFlagged > 0 ? '/movements?needsReview=true' : undefined}
        />
        <Tile
          tone={result.rowsStaged > 0 ? 'warning' : 'neutral'}
          icon={<ListChecks className="h-5 w-5 text-amber-700 dark:text-amber-300" />}
          label="Staged for transfer"
          value={result.rowsStaged}
          to={result.rowsStaged > 0 ? '/review?tab=transfers' : undefined}
        />
        <Tile
          tone={result.rowsFailed > 0 ? 'danger' : 'neutral'}
          icon={<CircleAlert className="h-5 w-5 text-destructive" />}
          label="Failed"
          value={result.rowsFailed}
        />
      </div>

      {result.errors.length > 0 ? (
        <Card>
          <CardContent className="space-y-2 py-5">
            <h2 className="text-sm font-medium">Row errors</h2>
            <ul className="list-disc pl-6 text-sm text-muted-foreground space-y-1">
              {result.errors.map((err, i) => (
                <li key={i}>{err}</li>
              ))}
            </ul>
          </CardContent>
        </Card>
      ) : null}

      {selectedProfileId === null ? (
        <SaveProfilePrompt fileFormat={fileFormat} mappings={mappings} />
      ) : null}

      <div className="flex items-center gap-2">
        <Button type="button" onClick={onImportAnother}>
          Import another file
        </Button>
        <Button
          type="button"
          variant="outline"
          nativeButton={false}
          render={<Link to="/movements">View transactions</Link>}
        />
      </div>
    </div>
  );
}

function Tile({
  tone,
  icon,
  label,
  value,
  to,
}: {
  tone: Tone;
  icon: React.ReactNode;
  label: string;
  value: number;
  to?: string;
}) {
  const body = (
    <div
      className={cn(
        'flex flex-col gap-1.5 rounded-lg border px-4 py-3',
        TONE[tone],
      )}
    >
      <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-muted-foreground font-medium">
        {icon}
        {label}
      </div>
      <div className="text-2xl font-semibold tabular-nums">{value}</div>
    </div>
  );
  return to ? (
    <Link to={to} className="rounded-lg focus:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2">
      {body}
    </Link>
  ) : body;
}
