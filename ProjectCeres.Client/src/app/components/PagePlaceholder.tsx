import { useEffect, useRef } from 'react';
import { Card, CardContent, CardHeader } from '@/components/ui/card';

type PagePlaceholderProps = {
  title: string;
  description: string;
};

/**
 * One-screen placeholder for routes whose real content lands in a later plan.
 * Focuses its heading on mount so screen-reader users hear the route change.
 */
export function PagePlaceholder({ title, description }: PagePlaceholderProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-2xl font-semibold outline-none"
        >
          {title}
        </h1>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">{description}</p>
      </CardContent>
    </Card>
  );
}
