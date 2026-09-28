import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SettingsSectionCard } from './SettingsSectionCard';

describe('SettingsSectionCard', () => {
  it('renders the title, description, and action children', () => {
    render(
      <SettingsSectionCard title="Your data" description="Download a copy.">
        <button>Export</button>
      </SettingsSectionCard>,
    );

    expect(screen.getByText('Your data')).toBeInTheDocument();
    expect(screen.getByText('Download a copy.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Export' })).toBeInTheDocument();
  });

  it('renders multiple action children side by side', () => {
    render(
      <SettingsSectionCard title="MFA" description="Two-factor is on.">
        <button>Regenerate</button>
        <button>Disable</button>
      </SettingsSectionCard>,
    );

    expect(screen.getByRole('button', { name: 'Regenerate' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Disable' })).toBeInTheDocument();
  });

  it('applies the default tone with no destructive tint by default', () => {
    const { container } = render(
      <SettingsSectionCard title="Your data" description="d">
        <button>X</button>
      </SettingsSectionCard>,
    );
    const card = container.querySelector('[data-slot="card"]') as HTMLElement;
    expect(card.className).not.toContain('border-destructive');
  });

  it('tints the whole card and the title for tone="destructive"', () => {
    const { container } = render(
      <SettingsSectionCard title="Danger zone" description="d" tone="destructive">
        <button>Erase</button>
      </SettingsSectionCard>,
    );
    const card = container.querySelector('[data-slot="card"]') as HTMLElement;
    expect(card.className).toContain('border-destructive/30');
    expect(card.className).toContain('bg-destructive/10');
    expect(screen.getByText('Danger zone').className).toContain('text-destructive');
  });
});
