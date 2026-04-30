import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';

type Props = {
  totalCount: number;
  pageSize: number;
};

export function MovementsPagination({ totalCount, pageSize }: Props) {
  const [params, setParams] = useSearchParams();
  const totalPages = Math.ceil(totalCount / pageSize);
  if (totalPages <= 1) return null;

  const currentPage = Number(params.get('page') ?? '1');

  function goToPage(page: number) {
    const next = new URLSearchParams(params);
    if (page === 1) next.delete('page');
    else next.set('page', String(page));
    setParams(next, { replace: true });
  }

  return (
    <nav aria-label="Pagination" className="flex items-center justify-center gap-1">
      {Array.from({ length: totalPages }, (_, i) => i + 1).map((page) => (
        <Button
          key={page}
          variant={page === currentPage ? 'default' : 'outline'}
          size="sm"
          aria-current={page === currentPage ? 'page' : undefined}
          onClick={() => goToPage(page)}
        >
          {page}
        </Button>
      ))}
    </nav>
  );
}
