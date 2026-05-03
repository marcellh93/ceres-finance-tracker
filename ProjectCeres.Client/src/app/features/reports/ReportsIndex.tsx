import { useEffect, useRef } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { BarChart3 } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { REPORT_META } from './reports-api';

export function ReportsIndex() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [params] = useSearchParams();
  useEffect(() => { headingRef.current?.focus(); }, []);

  return (
    <div className="space-y-6">
      <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold outline-none">
        Reports
      </h1>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {REPORT_META.map(({ slug, label, description }) => (
          <Link
            key={slug}
            to={`/reports/${slug}?${params.toString()}`}
            className="group no-underline"
            style={{ viewTransitionName: `report-card-${slug}` }}
          >
            <Card className="h-full transition-colors group-hover:border-primary/50 group-hover:bg-accent/30">
              <CardContent className="flex h-full flex-col gap-3 pt-6">
                <BarChart3 className="h-6 w-6 text-primary" aria-hidden="true" />
                <div>
                  <p className="font-medium">{label}</p>
                  <p className="mt-1 text-sm text-muted-foreground">{description}</p>
                </div>
              </CardContent>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
