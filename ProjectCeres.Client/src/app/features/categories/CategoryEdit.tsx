import { toast } from 'sonner';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { CategoryForm } from './CategoryForm';
import {
  CATEGORY_BY_ID_URL,
  CATEGORY_TYPES_URL,
  type CategoryDetailDto,
  type CategoryFormValues,
  type CategoryTypeDto,
  type UpdateCategoryRequest,
} from './categories-api';

type LayoutContext = { refetch: () => void };

export function CategoryEdit() {
  useDocumentTitle('Edit Category');
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();

  const detail = useApi<CategoryDetailDto>(id ? CATEGORY_BY_ID_URL(id) : '/api/categories/__missing__');
  const types = useApi<CategoryTypeDto[]>(CATEGORY_TYPES_URL);

  if (detail.loading || types.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (detail.error || !detail.data) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            That category doesn't exist.
          </div>
          <Button
            variant="outline"
            nativeButton={false}
            className="mt-4"
            render={<Link to="/categories">Back to Categories</Link>}
          />
        </CardContent>
      </Card>
    );
  }

  if (types.error || !types.data) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            Couldn't load category types.
          </div>
        </CardContent>
      </Card>
    );
  }

  const initialValues: CategoryFormValues = {
    name: detail.data.name,
    categoryTypeId: detail.data.categoryTypeId,
    lifestyleTag: detail.data.lifestyleTag,
  };

  async function handleSubmit(values: CategoryFormValues) {
    const body: UpdateCategoryRequest = {
      name: values.name.trim(),
      lifestyleTag: values.lifestyleTag,
    };
    try {
      const response = await fetch(CATEGORY_BY_ID_URL(id!), {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Saved.');
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
      <CardHeader><CardTitle>Edit category</CardTitle></CardHeader>
      <CardContent>
        <CategoryForm
          mode="edit"
          initialValues={initialValues}
          categoryTypes={types.data}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/categories')}
        />
      </CardContent>
    </Card>
  );
}
