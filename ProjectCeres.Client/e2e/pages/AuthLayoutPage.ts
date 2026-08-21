import type { Page } from '@playwright/test';

/**
 * Shared base for the public auth routes (AuthLayout in src/app/layout/AuthLayout.tsx).
 * Holds locators / actions that are layout-level rather than form-level.
 *
 * Subclasses (LoginPage, RegisterPage, etc.) extend this and add their
 * form-specific locators. Grow per feature stage — don't over-engineer 9.5a.
 */
export class AuthLayoutPage {
  protected readonly page: Page;
  constructor(page: Page) { this.page = page; }

  // Stage 11 Task 7 moved the SPA from /app/* to the site root; /app/* now 301s
  // to /* (see Program.cs UseRewriter). Routes are therefore root-relative.
  protected readonly basePath = '';

  async gotoRoute(route: string): Promise<void> {
    const path = `${this.basePath}${route}`;
    await this.page.goto(path, { waitUntil: 'networkidle' });
  }
}
