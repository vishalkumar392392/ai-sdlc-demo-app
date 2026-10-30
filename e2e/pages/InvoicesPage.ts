import { expect, type Locator, type Page } from '@playwright/test';

export class InvoicesPage {
  constructor(private readonly page: Page) {}

  async open() {
    await this.page.getByRole('tab', { name: 'Invoices' }).click();
  }

  rowFor(customerName: string): Locator {
    return this.page.getByRole('row').filter({ hasText: customerName });
  }

  /** Creates a draft invoice and submits it for approval. Returns its generated id. */
  async createAndSubmit(customerName: string, amount: string): Promise<string> {
    await this.open();
    await this.page.getByLabel('Customer name').fill(customerName);
    await this.page.getByLabel('Amount').fill(amount);
    await this.page.getByRole('button', { name: 'Create invoice' }).click();

    const row = this.rowFor(customerName);
    const submit = row.getByRole('button', { name: /^Submit invoice .+ for approval$/ });
    const label = (await submit.getAttribute('aria-label')) ?? '';
    const invoiceId = label.replace(/^Submit invoice (\S+) for approval$/, '$1');

    await submit.click();
    await expect(row.getByRole('cell', { name: 'Pending Approval' })).toBeVisible();
    return invoiceId;
  }
}
