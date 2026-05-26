import type { Page } from '@playwright/test';
import { AuthLayoutPage } from './AuthLayoutPage';

export class RegisterPage extends AuthLayoutPage {
  constructor(page: Page) {
    super(page);
  }

  async goto(): Promise<void> {
    await this.gotoRoute('/register');
  }

  email() {
    return this.page.getByLabel(/email/i);
  }
  password() {
    return this.page.getByLabel(/^password$/i);
  }
  passwordConfirm() {
    return this.page.getByLabel(/confirm password|password.*confirm/i);
  }
  submit() {
    return this.page.getByRole('button', { name: /sign up|register|create account/i });
  }
  verificationSentBlock() {
    return this.page.getByText(/check your inbox|verification email/i);
  }
}
