import { Download } from 'lucide-react';
import { toast } from 'sonner';
import { buttonVariants } from '@/components/ui/button';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import { buildCsvHref } from './csv-export';

type Props = {
  slug: string;
  queryString: string;
  children: React.ReactNode;
};

export function ReportTableCard({ slug, queryString, children }: Props) {
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
    </Card>
  );
}
