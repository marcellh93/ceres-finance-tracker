import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, it } from 'vitest';
import { AuthLayout } from './AuthLayout';
import { expectNoA11yViolations } from '../lib/test-axe';

describe('AuthLayout a11y', () => {
  it('renders without serious or critical axe violations', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/login']}>
        <Routes>
          <Route element={<AuthLayout />}>
            <Route path="login" element={<main><h1>Test page</h1></main>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    await expectNoA11yViolations(container);
  });
});
