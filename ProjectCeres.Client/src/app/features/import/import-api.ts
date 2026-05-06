// ---------- URL builders ----------

export const IMPORT_URL          = '/api/import';
export const IMPORT_HEADERS_URL  = '/api/import/headers';

export const IMPORT_PROFILES_URL                = '/api/import-profiles';
export const IMPORT_PROFILE_BY_ID_URL           = (id: string) => `/api/import-profiles/${id}`;
export const IMPORT_PROFILE_RECOVER_URL         = (id: string) => `/api/import-profiles/${id}/recover`;

export function buildProfilesListUrl(includeDeleted: boolean): string {
  return includeDeleted ? `${IMPORT_PROFILES_URL}?includeDeleted=true` : IMPORT_PROFILES_URL;
}

// ---------- Wire DTOs (camelCase per ASP.NET default JSON serializer) ----------

export type ImportFormat = 'Csv' | 'Excel';

/**
 * Mirrors ProjectCeres/ViewModels/CsvColumnMappings.cs (`ImportColumnMappings`).
 * `flipDebitSign` is legacy — parsers no longer act on it. Always send `true`
 * from the SPA; never expose in UI.
 */
export type ImportColumnMappings = {
  dateColumn: string;
  amountColumn: string;
  descriptionColumn: string;
  categoryColumn: string | null;
  flipDebitSign: boolean;
  sheetName: string | null;
};

export type HeaderDetectionResult = {
  headers: string[];
  dateColumn: string | null;
  amountColumn: string | null;
  descriptionColumn: string | null;
  categoryColumn: string | null;
};

export type ImportResult = {
  rowsImported:   number;
  rowsReconciled: number;
  rowsFlagged:    number;
  rowsStaged:     number;
  rowsFailed:     number;
  errors:         string[];
};

export type ImportProfileListItemDto = {
  id:              string;
  name:            string;
  format:          ImportFormat;
  sheetName:       string | null;
  mappings:        ImportColumnMappings;
  createdAt:       string;          // ISO 8601
  deletedAt:       string | null;
  daysUntilPurge:  number;
};

export type CreateImportProfileRequest = {
  name:     string;
  format:   ImportFormat;
  mappings: ImportColumnMappings;
};

export type UpdateImportProfileRequest = {
  name:     string;
  mappings: ImportColumnMappings;
};

// ---------- Form values (UI layer) ----------

export type ProfileFormValues = {
  name:              string;
  format:            ImportFormat;     // editable on Create, read-only on Edit
  sheetName:         string;           // empty string = null on the wire (Excel only)
  dateColumn:        string;
  amountColumn:      string;
  descriptionColumn: string;
  categoryColumn:    string;           // empty string = null on the wire
};

// ---------- Helpers ----------

/** Infer the import format from a filename's extension. Defaults to Csv. */
export function inferFormatFromFilename(filename: string): ImportFormat {
  return filename.toLowerCase().endsWith('.xlsx') ? 'Excel' : 'Csv';
}

/** Build the multipart body for POST /api/import. */
export function buildImportFormData(args: {
  file: File;
  accountId: string;
  mappings: ImportColumnMappings;
}): FormData {
  const fd = new FormData();
  fd.append('File', args.file);
  fd.append('AccountId', args.accountId);
  fd.append('DateColumn',        args.mappings.dateColumn);
  fd.append('AmountColumn',      args.mappings.amountColumn);
  fd.append('DescriptionColumn', args.mappings.descriptionColumn);
  if (args.mappings.categoryColumn) {
    fd.append('CategoryColumn',  args.mappings.categoryColumn);
  }
  fd.append('FlipDebitSign', String(args.mappings.flipDebitSign));
  return fd;
}

// ---------- Error envelope ----------

export type ApiErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details: unknown[];
  };
};
