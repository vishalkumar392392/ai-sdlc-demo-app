# Proposal

## Why

Today any user with the `Approver` or `Admin` role can approve an invoice of any amount on their own, so there is no spending control and no separation of duties. The business needs approval authority tied to role and amount, with a second, independent Director sign-off for the largest invoices.

## What Changes

- Introduce role-based approval limits: a Manager may approve invoices up to and including 50,000; a Director up to and including 500,000.
- Invoices over 500,000 need two distinct approvers: the first approval (Manager or Director) leaves the invoice `PendingApproval` with one recorded approval; the second approval must come from a Director who is not the first approver, and makes it `Approved`.
- The same user (usernames compared trimmed and case-insensitively) can never count as both approvers on one invoice.
- A rejected approval attempt returns HTTP 403 with a clear reason that names the role, the limit and what is needed instead, and changes nothing about the invoice.
- Each invoice records its approvals (approver, role, time) for audit and for the distinct-approver check.
- **BREAKING**: The `Approver` role and the `demo.approver` account are removed. New roles `Manager` and `Director`; demo users become `demo.manager`, `demo.director`, `demo.director2` and `demo.admin`.
- **BREAKING**: `Admin` (and any role without a limit) can no longer approve invoices. Approve requires Manager or Director; declining an invoice remains open to Manager, Director and Admin.
- The web UI shows "1 of 2 approvals" for invoices awaiting a second approval, and shows the rejection reason in the existing decision error alert.

## Capabilities

### New Capabilities
- `invoice-approval`: who may approve an invoice and with what outcome, based on role, amount limits, the two-approver rule above 500,000, distinct approvers, and the guarantee that a rejected attempt leaves the invoice unchanged.

### Modified Capabilities
<!-- None: no existing specs under openspec/specs/. -->

## Impact

- Domain (`backend/src/InvoiceApp.Domain`): new approval policy, new exception type for rejected attempts, new approval record on `Invoice`, `ApprovalService.Approve` now needs the approver's role and validates before mutating.
- API (`backend/src/InvoiceApp.Api`): `ApprovalsController` role restrictions and passing the session role; `ApiControllerBase.Handle` maps the new exception to 403; `appsettings.json` demo users.
- Web UI (`wwwroot/app.js`): approval progress label; error alert already present.
- Tests: new and updated xUnit tests in `backend/tests/InvoiceApp.Tests`; first Playwright test and page object under `e2e/`.
- Anyone signing in as `demo.approver` must switch to `demo.manager` or `demo.director`.
