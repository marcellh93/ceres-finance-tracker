import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { ReportTableCard } from './ReportTableCard';

describe('ReportTableCard pagination', () => {
  it('renders without pagination by default', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString=""><div>content</div></ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.queryByRole('button', { name: /previous/i })).not.toBeInTheDocument();
  });

  it('renders pagination controls when prop is provided', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 2, totalPages: 5, onNext: vi.fn(), onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.getByText('Page 2 of 5')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /previous/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /next/i })).toBeInTheDocument();
  });

  it('calls onNext when next button clicked', async () => {
    const onNext = vi.fn();
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 1, totalPages: 3, onNext, onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(onNext).toHaveBeenCalledOnce();
  });

  it('disables prev on first page', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 1, totalPages: 3, onNext: vi.fn(), onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();
  });

  it('disables next on last page', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 3, totalPages: 3, onNext: vi.fn(), onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
  });

  it('calls onPrev when prev button clicked', async () => {
    const onPrev = vi.fn();
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 2, totalPages: 3, onNext: vi.fn(), onPrev }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    await userEvent.click(screen.getByRole('button', { name: /previous/i }));
    expect(onPrev).toHaveBeenCalledOnce();
  });
});
