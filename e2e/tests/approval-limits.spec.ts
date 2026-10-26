import { expect, test } from '@playwright/test';
import { ApprovalsPage } from '../pages/ApprovalsPage';
import { InvoicesPage } from '../pages/InvoicesPage';
import { SignInPage } from '../pages/SignInPage';

test('a Manager approving an invoice over 50,000 is refused with a reason and the invoice stays pending', async ({ page }) => {
  const customer = `E2E Over Limit ${Date.now()}`;
  await new SignInPage(page).signInAs('demo.manager');

  const invoices = new InvoicesPage(page);
  const invoiceId = await invoices.createAndSubmit(customer, '50000.01');

  const approvals = new ApprovalsPage(page);
  await approvals.open();
  await expect(approvals.pendingEntry(invoiceId)).toBeVisible();

  await approvals.approve(invoiceId);

  await expect(approvals.decisionError).toBeVisible();
  await expect(approvals.decisionError).toContainText('Manager approval limit is 50,000.00');
  await expect(approvals.decisionError).toContainText('needs a Director');

  // Nothing changed: still waiting for approval, in the list and in the table.
  await expect(approvals.pendingEntry(invoiceId)).toBeVisible();
  await invoices.open();
  await expect(invoices.rowFor(customer).getByRole('cell', { name: 'Pending Approval' })).toBeVisible();
});
