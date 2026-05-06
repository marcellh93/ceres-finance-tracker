import { useState } from 'react';
import { toast } from 'sonner';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  IMPORT_PROFILES_URL,
  type CreateImportProfileRequest,
  type ImportColumnMappings,
  type ImportFormat,
} from './import-api';

type Props = {
  fileFormat: ImportFormat;
  mappings: ImportColumnMappings;
};

export function SaveProfilePrompt({ fileFormat, mappings }: Props) {
  const [name, setName] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [savedAs, setSavedAs] = useState<string | null>(null);
  const [dismissed, setDismissed] = useState(false);

  if (dismissed) return null;

  if (savedAs !== null) {
    return (
      <Card>
        <CardContent className="py-5 text-sm text-muted-foreground">
          Saved as <span className="font-medium text-foreground">{savedAs}</span>.
          You can pick it on your next import.
        </CardContent>
      </Card>
    );
  }

  async function handleSave(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (name.trim().length === 0 || submitting) return;
    setSubmitting(true);

    const body: CreateImportProfileRequest = {
      name:   name.trim(),
      format: fileFormat,
      mappings: {
        ...mappings,
        flipDebitSign: true,
      },
    };

    try {
      const response = await fetch(IMPORT_PROFILES_URL, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(body),
      });
      if (response.ok) {
        toast.success('Profile saved.');
        setSavedAs(name.trim());
      } else {
        toast.error("Couldn't save the profile. Try again.");
      }
    } catch {
      toast.error("Couldn't save the profile. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Save these settings as a profile?</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSave} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="save-profile-name">Profile name</Label>
            <Input
              id="save-profile-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Sabadell Checking"
              maxLength={100}
              required
            />
            <p className="text-xs text-muted-foreground">
              Reuse it on your next import. You can edit or archive it later
              from Import profiles.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Button type="submit" disabled={submitting || name.trim().length === 0}>
              {submitting ? 'Saving…' : 'Save'}
            </Button>
            <Button type="button" variant="outline" onClick={() => setDismissed(true)} disabled={submitting}>
              Skip
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
