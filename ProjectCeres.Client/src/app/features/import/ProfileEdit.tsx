import { toast } from 'sonner';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { ProfileForm } from './ProfileForm';
import {
  IMPORT_PROFILE_BY_ID_URL,
  type ImportProfileListItemDto,
  type ProfileFormValues,
  type UpdateImportProfileRequest,
} from './import-api';
import { apiFetch } from '../../lib/api-client';

type LayoutContext = { refetch: () => void };

export function ProfileEdit() {
  useDocumentTitle('Edit Import Profile');
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();

  const detail = useApi<ImportProfileListItemDto>(
    id ? IMPORT_PROFILE_BY_ID_URL(id) : '/api/import-profiles/__missing__',
  );

  if (detail.loading) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit profile</CardTitle></CardHeader>
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
        <CardHeader><CardTitle>Edit profile</CardTitle></CardHeader>
        <CardContent>
          <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
            That profile doesn't exist.
          </div>
          <Button
            variant="outline"
            nativeButton={false}
            className="mt-4"
            render={<Link to="/import/profiles">Back to Profiles</Link>}
          />
        </CardContent>
      </Card>
    );
  }

  const initialValues: ProfileFormValues = {
    name:              detail.data.name,
    format:            detail.data.format,
    sheetName:         detail.data.sheetName ?? '',
    dateColumn:        detail.data.mappings.dateColumn,
    amountColumn:      detail.data.mappings.amountColumn,
    descriptionColumn: detail.data.mappings.descriptionColumn,
    categoryColumn:    detail.data.mappings.categoryColumn ?? '',
  };

  async function handleSubmit(values: ProfileFormValues) {
    const sheetName = values.format === 'Excel' && values.sheetName.trim().length > 0
      ? values.sheetName.trim()
      : null;

    const body: UpdateImportProfileRequest = {
      name: values.name.trim(),
      mappings: {
        dateColumn:        values.dateColumn.trim(),
        amountColumn:      values.amountColumn.trim(),
        descriptionColumn: values.descriptionColumn.trim(),
        categoryColumn:    values.categoryColumn.trim() || null,
        flipDebitSign:     true,
        sheetName,
      },
    };

    try {
      const response = await apiFetch(IMPORT_PROFILE_BY_ID_URL(id!), {
        method: 'PATCH',
        body,
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Saved.');
      ctx?.refetch();
      navigate('/import/profiles');
      return { ok: true as const };
    } catch {
      toast.error("Couldn't save. Try again.");
      return { ok: false as const };
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>Edit profile</CardTitle></CardHeader>
      <CardContent>
        <ProfileForm
          mode="edit"
          initialValues={initialValues}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/import/profiles')}
        />
      </CardContent>
    </Card>
  );
}
