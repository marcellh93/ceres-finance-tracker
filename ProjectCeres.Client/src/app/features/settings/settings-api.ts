// ---------- URL builders ----------

export const SETTINGS_URL = '/api/settings';
export const CURRENCIES_URL = '/api/currencies';

// ---------- DTOs ----------

export type NumberFormat = 'comma_decimal' | 'period_decimal';
export type DateFormat = 'DD/MM/YYYY' | 'MM/DD/YYYY' | 'YYYY-MM-DD';

export type SettingsDto = {
  numberFormat: NumberFormat;
  dateFormat: DateFormat;
  defaultCurrencyCode: string;
  defaultCurrencySymbol: string;
  periodStartDay: number;
};

/**
 * The currencies endpoint serves only id/code/symbol — there is no `name`
 * field on the wire (verified against `CurrenciesApiController` on
 * 2026-05-02). Don't be tempted to add one without server-side support.
 */
export type CurrencyOptionDto = {
  id: number;
  code: string;
  symbol: string;
};

export type UpdateSettingsRequest = {
  numberFormat: NumberFormat;
  dateFormat: DateFormat;
  defaultCurrencyId: number;
  periodStartDay: number;
};

// ---------- Form values (UI layer) ----------

/**
 * The form holds the values the user is editing. It tracks
 * `defaultCurrencyId` directly because the dropdown's value is the id;
 * the server's GET response uses `defaultCurrencyCode` for display.
 */
export type SettingsFormValues = {
  numberFormat: NumberFormat;
  dateFormat: DateFormat;
  defaultCurrencyId: number;
  periodStartDay: number;
};
