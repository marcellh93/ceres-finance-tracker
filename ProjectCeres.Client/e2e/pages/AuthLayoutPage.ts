import type { Page } from '@playwright/test';

/**
 * Shared base for the public auth routes (AuthLayout in src/app/layout/AuthLayout.tsx).
 * Holds locators / actions that are layout-level rather than form-level.
 *
 * Subclasses (LoginPage, RegisterPage, etc.) extend this and add their
 * form-specific locators. Grow per feature stage — don't over-engineer 9.5a.
 */
export class AuthLayoutPage {
  constructor(protected readonly page: Page) {}

  // React Router basename is "/app" (see ProjectCeres.Client/src/app/main.tsx).
  protected readonly basePath = '/app';

  async gotoRoute(route: string): Promise<void> {
    const path = route.startsWith(this.basePath) ? route : `${this.basePath}${route}`;
    await this.page.goto(path, { waitUntil: 'networkidle' });
  }
}
