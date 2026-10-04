import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { Footer } from './Footer';
import * as events from '../i18n/consent-events';

function renderFooter() {
  return render(
    <MemoryRouter>
      <Footer />
    </MemoryRouter>,
  );
}

describe('Footer', () => {
  it('renders the three legal links with correct targets', () => {
    renderFooter();
    expect(screen.getByRole('link', { name: /privacy/i })).toHaveAttribute('href', '/privacy');
    expect(screen.getByRole('link', { name: /legal notice/i })).toHaveAttribute(
      'href',
      '/legal#aviso-legal',
    );
    expect(screen.getByRole('link', { name: /cookie policy/i })).toHaveAttribute(
      'href',
      '/legal#cookies',
    );
  });

  it('manage-cookies button fires openConsentManager', async () => {
    const spy = vi.spyOn(events, 'openConsentManager');
    renderFooter();
    await userEvent.click(screen.getByRole('button', { name: /manage cookies/i }));
    expect(spy).toHaveBeenCalledOnce();
  });
});
