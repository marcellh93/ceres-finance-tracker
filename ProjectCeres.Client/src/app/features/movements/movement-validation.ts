import type { ApiErrorEnvelope } from './movements-api';

/**
 * Parse the project's 422 error envelope into per-field error messages.
 * Per docs/api-contract.md, two shapes share the VALIDATION_ERROR code:
 *  - ModelState: details[] = [{ field: "Amount", message: "..." }]
 *  - Business-rule: details[] = [] (message lives in error.message)
 *
 * Returns a flat record where keys are camelCased field names. The special
 * key "_form" carries the top-level message when there are no per-field details.
 */
export function parseValidationErrors(body: unknown): Record<string, string> {
  const result: Record<string, string> = {};
  if (!body || typeof body !== 'object') return result;

  const envelope = body as Partial<ApiErrorEnvelope>;
  const error = envelope.error;
  if (!error || typeof error !== 'object') return result;

  if (Array.isArray(error.details) && error.details.length > 0) {
    for (const item of error.details) {
      if (!item.field || !item.message) continue;
      const camelKey = item.field.charAt(0).toLowerCase() + item.field.slice(1);
      result[camelKey] = item.message;
    }
    return result;
  }

  if (error.message) {
    result._form = error.message;
  }
  return result;
}
