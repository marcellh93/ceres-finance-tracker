import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthLayout } from './AuthLayout';

describe('AuthLayout', () => {
  it('uses bg-background for the page surface so the card has light-mode separation', () => {
    // Regression test for Stage 9.1.5.c. Previously used bg-muted/30, which
    // composited to ~oklch(0.989) in light mode against a card of oklch(1.000)
    // — card edge invisible. bg-background (1.000) lets the card's border + shadow
    // do the separation, which is the canonical shadcn pattern.
    const { container } = render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<AuthLayout />}>
            <Route index element={<div>test</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    const pageDiv = container.querySelector('.min-h-dvh');
    expect(pageDiv).not.toBeNull();
    expect(pageDiv).toHaveClass('bg-background');
    expect(pageDiv).not.toHaveClass('bg-muted/30');
    expect(pageDiv).not.toHaveClass('bg-muted');
  });
});
