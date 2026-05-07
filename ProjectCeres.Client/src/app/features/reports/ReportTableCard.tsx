import { ChevronLeft, ChevronRight, Download } from 'lucide-react';
import { toast } from 'sonner';
import { buttonVariants } from '@/components/ui/button';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import { buildCsvHref } from './csv-export';

type PaginationProps = {
  currentPage: number;
  totalPages: number;
  onNext: () => void;
  onPrev: () => void;
};

type Props = {
  slug: string;
  queryString: string;
  children: React.ReactNode;
  pagination?: PaginationProps;
};

export function ReportTableCard({ slug, queryString, children, pagination }: Props) {
  const csvHref = buildCsvHref(slug, queryString);

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-2">
        <span className="text-sm font-medium text-muted-foreground">Results</span>
        <a
          href={csvHref}
          rel="noopener"
          onClick={() => toast.info('Exporting report…')}
          className={cn(buttonVariants({ variant: 'outline', size: 'sm' }), 'gap-2 no-underline')}
        >
          <Download className="h-4 w-4" aria-hidden="true" />
          Export CSV
        </a>
      </CardHeader>
      <CardContent className="overflow-x-auto">
        {children}
      </CardContent>
      {pagination && (
        <div className="flex items-center justify-end gap-3 border-t border-border px-4 py-2">
          <button
            type="button"
            onClick={pagination.onPrev}
            disabled={pagination.currentPage === 1}
            aria-label="Previous page"
            className="flex h-7 w-7 items-center justify-center rounded-md border border-border bg-background text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:pointer-events-none disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" aria-hidden="true" />
          </button>
          <span className="text-sm text-muted-foreground">
            Page {pagination.currentPage} of {pagination.totalPages}
          </span>
          <button
            type="button"
            onClick={pagination.onNext}
            disabled={pagination.currentPage === pagination.totalPages}
            aria-label="Next page"
            className="flex h-7 w-7 items-center justify-center rounded-md border border-border bg-background text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:pointer-events-none disabled:opacity-40"
          >
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      )}
    </Card>
  );
}
