// ---------- URL builders ----------

export const CATEGORIES_URL = '/api/categories';
export const CATEGORY_BY_ID_URL = (id: string) => `/api/categories/${id}`;
export const CATEGORY_ARCHIVE_URL = (id: string) => `/api/categories/${id}/archive`;
export const CATEGORY_REACTIVATE_URL = (id: string) => `/api/categories/${id}/reactivate`;
export const CATEGORY_TYPES_URL = '/api/category-types';

export function buildListUrl(includeInactive: boolean): string {
  return includeInactive ? `${CATEGORIES_URL}?includeInactive=true` : CATEGORIES_URL;
}

// ---------- DTOs ----------

export type LifestyleTag = 'Needs' | 'Wants' | 'Savings';

export type CategoryListItemDto = {
  id: string;
  name: string;
  categoryTypeId: number;
  categoryTypeName: string;       // "Income" | "Expense"
  lifestyleTag: LifestyleTag | null;
  isActive: boolean;
  isSystem: boolean;
};

export type CategoryDetailDto = CategoryListItemDto;

export type CategoryTypeDto = {
  id: number;
  name: string;                   // "Income" | "Expense"
};

export type CreateCategoryRequest = {
  name: string;
  categoryTypeId: number;
  lifestyleTag: LifestyleTag | null;
};

/**
 * The PATCH body deliberately omits categoryTypeId. The server's
 * UpdateCategoryRequest does not bind that field — verified in
 * ProjectCeres/ViewModels/CategoryDtos.cs on 2026-05-02. CategoryType
 * is structurally immutable on the wire; the form's read-only display
 * on Edit is a UX hint, not a security boundary.
 */
export type UpdateCategoryRequest = {
  name: string;
  lifestyleTag: LifestyleTag | null;
};

// ---------- Form values (UI layer) ----------

export type CategoryFormValues = {
  name: string;
  categoryTypeId: number;         // editable on Create, read-only display on Edit
  lifestyleTag: LifestyleTag | null;
};

// ---------- Reserved system-category ids ----------

/**
 * IDs the API marks as system OR reserved fallback categories. Used by
 * the SPA to suppress row actions and render the System badge. Mirrors
 * the server-side rule in CategoryPolicies.IsReserved.
 */
export const RESERVED_UNCATEGORIZED_IDS = new Set<string>([
  '20000000-0000-0000-0000-000000000025',  // Uncategorized Income
  '20000000-0000-0000-0000-000000000026',  // Uncategorized Expense
]);

export function isLockedCategory(c: { id: string; isSystem: boolean }): boolean {
  return c.isSystem || RESERVED_UNCATEGORIZED_IDS.has(c.id);
}

// ---------- Error envelope ----------

export type ApiErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details: unknown[];
  };
};
