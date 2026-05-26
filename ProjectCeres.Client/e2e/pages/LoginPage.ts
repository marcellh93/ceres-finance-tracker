import type { Page } from '@playwright/test';
import { AuthLayoutPage } from './AuthLayoutPage';

export class LoginPage extends AuthLayoutPage {
  constructor(page: Page) {
    super(page);
  }

  async goto(): Promise<void> {
    await this.gotoRoute('/login');
  }

  // Locators kept skeletal — grow with the form's accessible-name surface as
  // feature stages exercise them.
  email() {
    return this.page.getByLabel(/email/i);
  }
  password() {
    return this.page.getByLabel(/password/i, { exact: false });
  }
  submit() {
    return this.page.getByRole('button', { name: /sign in|log in/i });
  }
}
