import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PagePlaceholder } from './PagePlaceholder';

describe('PagePlaceholder', () => {
  it('renders the title as an h1', () => {
    render(<PagePlaceholder title="Dashboard" description="Coming soon." />);
    const heading = screen.getByRole('heading', { level: 1, name: 'Dashboard' });
    expect(heading).toBeDefined();
  });

  it('renders the description', () => {
    render(<PagePlaceholder title="X" description="The Movements page lands later." />);
    expect(screen.getByText('The Movements page lands later.')).toBeDefined();
  });

  it('focuses the heading on mount so screen readers announce navigation', () => {
    render(<PagePlaceholder title="Reports" description="…" />);
    const heading = screen.getByRole('heading', { level: 1 });
    expect(document.activeElement).toBe(heading);
  });

  it('marks the heading as programmatically focusable but not in the tab order', () => {
    render(<PagePlaceholder title="X" description="…" />);
    const heading = screen.getByRole('heading', { level: 1 });
    expect(heading.getAttribute('tabIndex')).toBe('-1');
  });
});
