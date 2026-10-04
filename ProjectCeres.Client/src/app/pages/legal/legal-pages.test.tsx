import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Privacy } from './Privacy';
import { Legal } from './Legal';

describe('legal pages', () => {
  it('Privacy renders the privacy heading structure', () => {
    render(<MemoryRouter><Privacy /></MemoryRouter>);
    expect(screen.getByRole('heading', { name: /what data we collect/i })).toBeInTheDocument();
  });

  it('Privacy states the retention windows (30-day grace period / day 180 permanent deletion)', () => {
    render(<MemoryRouter><Privacy /></MemoryRouter>);
    expect(screen.getByText(/30[- ]day/i)).toBeInTheDocument();
    expect(screen.getByText(/day 180/i)).toBeInTheDocument();
  });

  it('Privacy states the 6-year financial-record retention that limits erasure', () => {
    // The correctness point a generic template misses (Art. 17(3)(b) exemption).
    render(<MemoryRouter><Privacy /></MemoryRouter>);
    expect(screen.getByText(/6[- ]year/i)).toBeInTheDocument();
    expect(screen.getAllByText(/erasure/i).length).toBeGreaterThan(0);
  });

  it('Legal renders both anchored sections', () => {
    const { container } = render(<MemoryRouter><Legal /></MemoryRouter>);
    expect(container.querySelector('#aviso-legal')).not.toBeNull();
    expect(container.querySelector('#cookies')).not.toBeNull();
  });

  it('legal content carries the not-legally-reviewed DRAFT banner', () => {
    render(<MemoryRouter><Legal /></MemoryRouter>);
    expect(screen.getByText(/not legally reviewed/i)).toBeInTheDocument();
  });

  it('Aviso Legal leaves business-identity facts as CONFIRM markers (never guessed)', () => {
    render(<MemoryRouter><Legal /></MemoryRouter>);
    expect(screen.getAllByText(/\[CONFIRM/i).length).toBeGreaterThan(0);
  });
});
