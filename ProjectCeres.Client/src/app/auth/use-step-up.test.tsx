import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useStepUp } from './use-step-up';
import { ReauthRequiredError } from '../lib/api-client';

describe('useStepUp', () => {
  it('returns the action result when no reauth is required', async () => {
    const { result } = renderHook(() => useStepUp());
    const action = async () => 'success';
    await expect(result.current.requireStepUp(action)).resolves.toBe('success');
  });

  it('rethrows ReauthRequiredError when no dialog is mounted (Phase 1 stub)', async () => {
    const { result } = renderHook(() => useStepUp());
    const action = async () => {
      throw new ReauthRequiredError('Please reauthenticate.');
    };
    await expect(result.current.requireStepUp(action)).rejects.toBeInstanceOf(ReauthRequiredError);
  });
});
