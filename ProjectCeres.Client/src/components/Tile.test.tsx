import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Tile } from './Tile';

describe('Tile', () => {
  it('renders children inside a styled div', () => {
    render(<Tile>Hello</Tile>);
    expect(screen.getByText('Hello')).toBeInTheDocument();
  });

  it('applies the default surface classes', () => {
    const { container } = render(<Tile>X</Tile>);
    const div = container.firstChild as HTMLElement;
    expect(div.className).toContain('rounded-md');
    expect(div.className).toContain('bg-muted/40');
    expect(div.className).toContain('p-4');
  });

  it('appends caller-provided className', () => {
    const { container } = render(<Tile className="custom-class">X</Tile>);
    const div = container.firstChild as HTMLElement;
    expect(div.className).toContain('custom-class');
    expect(div.className).toContain('bg-muted/40');
  });
});
