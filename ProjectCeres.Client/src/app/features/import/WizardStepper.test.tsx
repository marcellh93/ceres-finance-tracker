import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { WizardStepper } from './WizardStepper';

describe('WizardStepper', () => {
  it('marks the current step with aria-current="step"', () => {
    render(<WizardStepper currentStep={2} onJumpBack={vi.fn()} />);
    const items = screen.getAllByRole('listitem');
    expect(items[0]).not.toHaveAttribute('aria-current');
    expect(items[1]).toHaveAttribute('aria-current', 'step');
  });

  it('renders completed steps as buttons', () => {
    render(<WizardStepper currentStep={3} onJumpBack={vi.fn()} />);
    expect(screen.getByRole('button', { name: /step 1: file/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /step 2: mapping/i })).toBeInTheDocument();
  });

  it('clicking a completed step calls onJumpBack with that step number', () => {
    const onJumpBack = vi.fn();
    render(<WizardStepper currentStep={3} onJumpBack={onJumpBack} />);
    fireEvent.click(screen.getByRole('button', { name: /step 1: file/i }));
    expect(onJumpBack).toHaveBeenCalledWith(1);
  });

  it('does not render future steps as buttons', () => {
    render(<WizardStepper currentStep={1} onJumpBack={vi.fn()} />);
    // Only the active label "File" is rendered; future steps are static text.
    expect(screen.queryByRole('button', { name: /step 1/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /step 2/i })).toBeNull();
  });
});
