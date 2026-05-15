import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { RequireAuth } from './RequireAuth';

describe('RequireAuth', () => {
  it('renders children when allowed (Phase 1 stub: always allow)', () => {
    render(
      <MemoryRouter>
        <RequireAuth>
          <div>protected</div>
        </RequireAuth>
      </MemoryRouter>,
    );
    expect(screen.getByText('protected')).toBeDefined();
  });

  // Note: the "redirect to /login when anon" assertion lives in Task 4's
  // auth-context tests, since RequireAuth gains its real gate then.
});
