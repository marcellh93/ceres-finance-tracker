import type { Page } from '@playwright/test';
import { expect } from '@playwright/test';
import { RegisterPage } from '../pages/RegisterPage';
import { LoginPage } from '../pages/LoginPage';

export interface Creds {
  email: string;
  password: string;
}

/**
 * Composed auth flows callable from specs OR from agent-walk.
 *
 * Scope for 9.5a: register → (email-verify | dashboard). Grow per feature
 * stage — login, password-reset, TOTP, account-unlock all get their own
 * composed flows when their stages land.
 */
export function register(page: Page, creds: Creds) {
  const registerPage = new RegisterPage(page);
  return {
    async expectLoggedIn(): Promise<void> {
      await registerPage.goto();
      await registerPage.email().fill(creds.email);
      await registerPage.password().fill(creds.password);
      const confirm = registerPage.passwordConfirm();
      if (await confirm.count()) await confirm.fill(creds.password);
      await registerPage.submit().click();
      // Project Ceres requires email verification before login, so the post-submit
      // surface is /email-verify in the common case. If the project ever changes
      // to auto-login on register, the dashboard path is the alternate landing.
      await page.waitForURL((url) => /\/(email-verify|$)/.test(url.pathname), {
        timeout: 10_000,
      });
    },
    async expectVerificationEmailSent(): Promise<void> {
      await registerPage.goto();
      await registerPage.email().fill(creds.email);
      await registerPage.password().fill(creds.password);
      const confirm = registerPage.passwordConfirm();
      if (await confirm.count()) await confirm.fill(creds.password);
      await registerPage.submit().click();
      await expect(registerPage.verificationSentBlock()).toBeVisible({ timeout: 10_000 });
    },
  };
}

export function login(page: Page, creds: Creds) {
  const loginPage = new LoginPage(page);
  return {
    async expectLoggedIn(): Promise<void> {
      await loginPage.goto();
      await loginPage.email().fill(creds.email);
      await loginPage.password().fill(creds.password);
      await loginPage.submit().click();
      await page.waitForURL((url) => /^\/$/.test(url.pathname), { timeout: 10_000 });
    },
  };
}
