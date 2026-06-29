import { useEffect, useState } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Command,
  CommandGroup,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { useApi } from '../../lib/use-api';
import {
  IMPORT_PROFILES_URL,
  type HeaderDetectionResult,
  type ImportColumnMappings,
  type ImportProfileListItemDto,
} from './import-api';

type MappingDraft = {
  dateColumn:        string;
  amountColumn:      string;
  descriptionColumn: string;
  categoryColumn:    string;
};

type Props = {
  headers: HeaderDetectionResult;
  selectedProfileId: string | null;
  initialMappings: ImportColumnMappings;
  fileName: string;
  onSelectProfile: (id: string | null, mappings: ImportColumnMappings | null) => void;
  onContinue: (mappings: ImportColumnMappings) => void;
  onBack: () => void;
};

export function StepMapping({
  headers,
  selectedProfileId,
  initialMappings,
  fileName,
  onSelectProfile,
  onContinue,
  onBack,
}: Props) {
  const profiles = useApi<ImportProfileListItemDto[]>(IMPORT_PROFILES_URL);

  const [draft, setDraft] = useState<MappingDraft>({
    dateColumn:        initialMappings.dateColumn,
    amountColumn:      initialMappings.amountColumn,
    descriptionColumn: initialMappings.descriptionColumn,
    categoryColumn:    initialMappings.categoryColumn ?? '',
  });

  // Re-seed the draft if the profile selection or initial mappings change.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: wizard form reset driven by parent profile-selection prop; the initialMappings object arrives from an external profile selection event, not a render.
    setDraft({
      dateColumn:        initialMappings.dateColumn,
      amountColumn:      initialMappings.amountColumn,
      descriptionColumn: initialMappings.descriptionColumn,
      categoryColumn:    initialMappings.categoryColumn ?? '',
    });
  }, [initialMappings]);

  const profileMissingColumns = useProfileMismatch({
    profileId: selectedProfileId,
    profiles:  profiles.data ?? [],
    headers:   headers.headers,
  });

  const valid =
    draft.dateColumn.trim() !== '' &&
    draft.amountColumn.trim() !== '' &&
    draft.descriptionColumn.trim() !== '';

  function handleSelectProfile(id: string | null) {
    if (id === null) {
      onSelectProfile(null, null);
      return;
    }
    const profile = profiles.data?.find((p) => p.id === id);
    if (!profile) return;
    onSelectProfile(id, profile.mappings);
  }

  function handleContinue() {
    if (!valid) return;
    onContinue({
      dateColumn:        draft.dateColumn.trim(),
      amountColumn:      draft.amountColumn.trim(),
      descriptionColumn: draft.descriptionColumn.trim(),
      categoryColumn:    draft.categoryColumn.trim() || null,
      flipDebitSign:     true,
      sheetName:         null,
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Map columns</CardTitle>
      </CardHeader>
      <CardContent className="space-y-6">
        <p className="text-sm text-muted-foreground">
          Match your file's column names to the fields below. We've pre-selected
          the closest matches in <span className="font-medium">{fileName}</span>.
        </p>

        <div className="space-y-1.5">
          <Label htmlFor="profile-picker">Saved profile</Label>
          {profiles.loading ? (
            <Skeleton className="h-9 w-full" />
          ) : (
            <ProfilePicker
              id="profile-picker"
              profiles={profiles.data ?? []}
              value={selectedProfileId}
              onChange={handleSelectProfile}
            />
          )}
          <p className="text-xs text-muted-foreground">
            Optional. Pick a saved profile to fill in the columns below
            automatically.
          </p>
        </div>

        {profileMissingColumns.length > 0 ? (
          <div
            role="status"
            className="rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900/40 dark:bg-amber-950/40 dark:text-amber-100"
          >
            This file is missing columns the profile expects:{' '}
            {profileMissingColumns.map((c) => `'${c}'`).join(', ')}. Adjust the
            mapping below before continuing.
          </div>
        ) : null}

        <ColumnField
          id="dateColumn"
          label="Date column"
          headers={headers.headers}
          value={draft.dateColumn}
          onChange={(v) => setDraft((cur) => ({ ...cur, dateColumn: v }))}
          required
        />
        <ColumnField
          id="amountColumn"
          label="Amount column"
          headers={headers.headers}
          value={draft.amountColumn}
          onChange={(v) => setDraft((cur) => ({ ...cur, amountColumn: v }))}
          required
        />
        <ColumnField
          id="descriptionColumn"
          label="Description column"
          headers={headers.headers}
          value={draft.descriptionColumn}
          onChange={(v) => setDraft((cur) => ({ ...cur, descriptionColumn: v }))}
          required
        />
        <ColumnField
          id="categoryColumn"
          label="Category column"
          headers={headers.headers}
          value={draft.categoryColumn}
          onChange={(v) => setDraft((cur) => ({ ...cur, categoryColumn: v }))}
          hint="Optional. Leave blank if your bank doesn't include a category column."
        />

        <div className="flex items-center gap-2 pt-2">
          <Button type="button" disabled={!valid} onClick={handleContinue}>
            Continue
          </Button>
          <Button type="button" variant="outline" onClick={onBack}>
            Back
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

// ---------------------------------------------------------------------------

function useProfileMismatch(args: {
  profileId: string | null;
  profiles: ImportProfileListItemDto[];
  headers: string[];
}): string[] {
  const { profileId, profiles, headers } = args;
  if (profileId === null || headers.length === 0) return [];
  const profile = profiles.find((p) => p.id === profileId);
  if (!profile) return [];
  const set = new Set(headers);
  const missing: string[] = [];
  if (profile.mappings.dateColumn        && !set.has(profile.mappings.dateColumn))        missing.push(profile.mappings.dateColumn);
  if (profile.mappings.amountColumn      && !set.has(profile.mappings.amountColumn))      missing.push(profile.mappings.amountColumn);
  if (profile.mappings.descriptionColumn && !set.has(profile.mappings.descriptionColumn)) missing.push(profile.mappings.descriptionColumn);
  if (profile.mappings.categoryColumn    && !set.has(profile.mappings.categoryColumn))    missing.push(profile.mappings.categoryColumn);
  return missing;
}

function ColumnField({
  id,
  label,
  headers,
  value,
  onChange,
  required,
  hint,
}: {
  id: string;
  label: string;
  headers: string[];
  value: string;
  onChange: (v: string) => void;
  required?: boolean;
  hint?: string;
}) {
  return (
    <div className="space-y-1.5">
      <Label htmlFor={id}>{label}</Label>
      {headers.length > 0 ? (
        <ColumnCombobox
          id={id}
          headers={headers}
          value={value}
          onChange={onChange}
          placeholder="Select a column"
        />
      ) : (
        <Input
          id={id}
          aria-label={label}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          required={required}
          maxLength={100}
          placeholder="Type the column name"
        />
      )}
      {hint ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
    </div>
  );
}

function ColumnCombobox({
  id,
  headers,
  value,
  onChange,
  placeholder,
}: {
  id: string;
  headers: string[];
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
}) {
  const [open, setOpen] = useState(false);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label={id}
            aria-expanded={open}
            className="w-full justify-between"
          >
            <span className={value ? '' : 'text-muted-foreground'}>
              {value || placeholder}
            </span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent
        className="p-0 w-(--anchor-width) min-w-(--anchor-width)"
        align="start"
      >
        <Command>
          <CommandList>
            <CommandGroup>
              <CommandItem
                value=""
                className="whitespace-nowrap"
                onSelect={() => {
                  onChange('');
                  setOpen(false);
                }}
              >
                <Check
                  className={cn('mr-2 h-4 w-4', value === '' ? 'opacity-100' : 'opacity-0')}
                />
                <span className="text-muted-foreground italic">— None —</span>
              </CommandItem>
              {headers.map((h) => (
                <CommandItem
                  key={h}
                  value={h}
                  className="whitespace-nowrap"
                  onSelect={() => {
                    onChange(h);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn('mr-2 h-4 w-4', value === h ? 'opacity-100' : 'opacity-0')}
                  />
                  {h}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

function ProfilePicker({
  id,
  profiles,
  value,
  onChange,
}: {
  id: string;
  profiles: ImportProfileListItemDto[];
  value: string | null;
  onChange: (id: string | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const active = profiles.filter((p) => p.deletedAt === null);
  const selected = active.find((p) => p.id === value);

  if (active.length === 0) {
    return (
      <div
        id={id}
        aria-label="Saved profile"
        className="flex h-9 w-full items-center rounded-md border border-input bg-muted/40 px-3 text-sm text-muted-foreground"
      >
        No saved profiles yet — fill the columns below manually.
      </div>
    );
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label="Saved profile"
            aria-expanded={open}
            className="w-full justify-between"
          >
            <span className={selected ? '' : 'text-muted-foreground'}>
              {selected ? selected.name : 'Map columns manually'}
            </span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent
        className="p-0 w-(--anchor-width) min-w-(--anchor-width)"
        align="start"
      >
        <Command>
          <CommandList>
            <CommandGroup>
              <CommandItem
                value=""
                className="whitespace-nowrap"
                onSelect={() => {
                  onChange(null);
                  setOpen(false);
                }}
              >
                <Check
                  className={cn('mr-2 h-4 w-4', value === null ? 'opacity-100' : 'opacity-0')}
                />
                <span className="text-muted-foreground italic">Map columns manually</span>
              </CommandItem>
              {active.map((p) => (
                <CommandItem
                  key={p.id}
                  value={p.name}
                  className="whitespace-nowrap"
                  onSelect={() => {
                    onChange(p.id);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn('mr-2 h-4 w-4', value === p.id ? 'opacity-100' : 'opacity-0')}
                  />
                  {p.name}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
