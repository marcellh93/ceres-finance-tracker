import { describe, expect, it } from 'vitest';
import { parseValidationErrors } from './movement-validation';

describe('parseValidationErrors', () => {
  it('returns field-keyed errors for ModelState shape', () => {
    const envelope = {
      error: {
        code: 'VALIDATION_ERROR',
        message: 'One or more fields are invalid.',
        details: [
          { field: 'Amount', message: 'Amount must be greater than zero.' },
          { field: 'AccountId', message: 'Please select an account.' },
        ],
      },
    };
    const result = parseValidationErrors(envelope);
    expect(result.amount).toBe('Amount must be greater than zero.');
    expect(result.accountId).toBe('Please select an account.');
    expect(result._form).toBeUndefined();
  });

  it('returns _form key for business-rule shape (empty details)', () => {
    const envelope = {
      error: {
        code: 'VALIDATION_ERROR',
        message: 'Source and destination accounts must be different.',
        details: [],
      },
    };
    const result = parseValidationErrors(envelope);
    expect(result._form).toBe('Source and destination accounts must be different.');
  });

  it('lowercases the first character of field names (PascalCase → camelCase)', () => {
    const envelope = {
      error: {
        code: 'VALIDATION_ERROR',
        message: '...',
        details: [{ field: 'SourceAccountId', message: 'Required.' }],
      },
    };
    const result = parseValidationErrors(envelope);
    expect(result.sourceAccountId).toBe('Required.');
  });

  it('returns empty object for non-error responses', () => {
    expect(parseValidationErrors(null)).toEqual({});
    expect(parseValidationErrors({})).toEqual({});
    expect(parseValidationErrors({ error: null })).toEqual({});
  });
});
