import { type Locator, type Page } from '@playwright/test';

export class ApprovalsPage {
  readonly decisionError: Locator;

  constructor(private readonly page: Page) {
    this.decisionError = page.getByRole('tabpanel', { name: 'Approvals' }).getByRole('alert');
  }

  async open() {
    await this.page.getByRole('tab', { name: 'Approvals' }).click();
  }

  pendingEntry(invoiceId: string): Locator {
    return this.page.getByRole('listitem').filter({ hasText: invoiceId });
  }

  /** Clicks Approve and returns the HTTP status the server answered with. */
  async approve(invoiceId: string): Promise<number> {
    const response = this.page.waitForResponse((r) => r.url().endsWith(`/api/approvals/${invoiceId}/approve`));
    await this.page.getByRole('button', { name: `Approve invoice ${invoiceId}`, exact: true }).click();
    return (await response).status();
  }
}
