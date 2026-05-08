import { toast } from 'sonner';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { ProfileForm } from './ProfileForm';
import {
  IMPORT_PROFILES_URL,
  type CreateImportProfileRequest,
  type ProfileFormValues,
} from './import-api';

type LayoutContext = { refetch: () => void };

const initialValues: ProfileFormValues = {
  name:              '',
  format:            'Csv',
  sheetName:         '',
  dateColumn:        '',
  amountColumn:      '',
  descriptionColumn: '',
  categoryColumn:    '',
};

export function ProfileCreate() {
  useDocumentTitle('New Import Profile');
  const navigate = useNavigate();
  const ctx = useOutletContext<LayoutContext>();

  async function handleSubmit(values: ProfileFormValues) {
    const sheetName = values.format === 'Excel' && values.sheetName.trim().length > 0
      ? values.sheetName.trim()
      : null;

    const body: CreateImportProfileRequest = {
      name:   values.name.trim(),
      format: values.format,
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
      const response = await fetch(IMPORT_PROFILES_URL, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(body),
      });
      if (!response.ok) {
        toast.error("Couldn't save. Try again.");
        return { ok: false as const };
      }
      toast.success('Created.');
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
      <CardHeader><CardTitle>New profile</CardTitle></CardHeader>
      <CardContent>
        <ProfileForm
          mode="create"
          initialValues={initialValues}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/import/profiles')}
        />
      </CardContent>
    </Card>
  );
}
