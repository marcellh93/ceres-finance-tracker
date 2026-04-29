import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Numeric } from './Numeric';

describe('Numeric', () => {
  it('renders the children', () => {
    render(<Numeric>1.234,56</Numeric>);
    expect(screen.getByText('1.234,56')).toBeDefined();
  });

  it('applies the mono font and tabular-nums class', () => {
    render(<Numeric>42%</Numeric>);
    const el = screen.getByText('42%');
    expect(el.className).toContain('font-mono');
    expect(el.className).toContain('tabular-nums');
  });

  it('preserves caller-supplied className', () => {
    render(<Numeric className="text-destructive">-€500</Numeric>);
    const el = screen.getByText('-€500');
    expect(el.className).toContain('text-destructive');
    expect(el.className).toContain('font-mono');
  });

  it('renders a span by default and supports `as`', () => {
    const { rerender } = render(<Numeric>1</Numeric>);
    expect(screen.getByText('1').tagName).toBe('SPAN');
    rerender(<Numeric as="td">2</Numeric>);
    expect(screen.getByText('2').tagName).toBe('TD');
  });
});
