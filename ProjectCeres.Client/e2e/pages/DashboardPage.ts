import type { Page } from '@playwright/test';

/**
 * Dashboard lives under the authenticated AppLayout (src/app/layout/AppLayout.tsx)
 * at the index route — i.e. `/app`. Locators stay skeletal for 9.5a; expand as
 * dashboard tests are written.
 */
export class DashboardPage {
  private readonly page: Page;
  constructor(page: Page) { this.page = page; }

  private readonly basePath = '/app';

  async goto(): Promise<void> {
    await this.page.goto(this.basePath, { waitUntil: 'networkidle' });
  }

  heading() {
    return this.page.getByRole('heading', { name: /dashboard|net worth/i });
  }
}
