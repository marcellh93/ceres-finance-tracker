import { toast } from 'sonner';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { CategoryForm } from './CategoryForm';
import {
  CATEGORIES_URL,
  CATEGORY_TYPES_URL,
  type CategoryFormValues,
  type CategoryTypeDto,
  type CreateCategoryRequest,
} from './categories-api';

type LayoutContext = { refetch: () => void };

const initialValues: CategoryFormValues = {
  name: '',
  categoryTypeId: 2,    // Expense default — most-frequent type.
  lifestyleTag: null,
};

export function CategoryCreate() {
  useDocumentTitle('New Category');
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();
  const types = useApi<CategoryTypeDto[]>(CATEGORY_TYPES_URL);

  if (types.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>New category</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data) {
    return (
      <Card>
        <CardHeader><CardTitle>New category</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load category types.
          </div>
        </CardContent>
      </Card>
    );
  }

  async function handleSubmit(values: CategoryFormValues) {
    const body: CreateCategoryRequest = {
      name: values.name.trim(),
      categoryTypeId: values.categoryTypeId,
      lifestyleTag: values.lifestyleTag,
    };
    try {
      const response = await fetch(CATEGORIES_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Created.');
      ctx?.refetch();
      navigate('/categories');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>New category</CardTitle></CardHeader>
      <CardContent>
        <CategoryForm
          mode="create"
          initialValues={initialValues}
          categoryTypes={types.data}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/categories')}
        />
      </CardContent>
    </Card>
  );
}
