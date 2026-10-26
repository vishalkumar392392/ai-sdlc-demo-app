import { type Locator, type Page } from '@playwright/test';

export class ApprovalsPage {
  readonly decisionError: Locator;

  constructor(private readonly page: Page) {
    this.decisionError = page.getByRole('alert');
  }

  async open() {
    await this.page.getByRole('tab', { name: 'Approvals' }).click();
  }

  pendingEntry(invoiceId: string): Locator {
    return this.page.getByRole('listitem').filter({ hasText: invoiceId });
  }

  async approve(invoiceId: string) {
    await this.page.getByRole('button', { name: `Approve invoice ${invoiceId}`, exact: true }).click();
  }
}
