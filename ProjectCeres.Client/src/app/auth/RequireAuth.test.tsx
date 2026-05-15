import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { RequireAuth } from './RequireAuth';

describe('RequireAuth', () => {
  it('renders children when allowed (Phase 1 stub: always allow)', () => {
    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route
            element={<RequireAuth><div>protected</div></RequireAuth>}
          >
            <Route path="dashboard" element={<div>protected</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByText('protected')).toBeDefined();
  });

  // Note: the "redirect to /login when anon" assertion lives in Task 4's
  // auth-context tests, since RequireAuth gains its real gate then.
});
