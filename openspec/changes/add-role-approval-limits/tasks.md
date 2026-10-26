# Tasks

Each numbered group lands as its own commit (Conventional Commits, body says why). Run `dotnet test backend/InvoiceApp.sln` before every commit; every commit builds and passes.

## 1. Single-approver limits (commit: `feat(approval): enforce Manager and Director approval limits`)

- [ ] 1.1 Add `ApprovalNotAllowedException(string reason)` in `InvoiceApp.Domain`; verify it builds and carries the message
- [ ] 1.2 Add pure `ApprovalPolicy` in `InvoiceApp.Domain` with inclusive `decimal` limits (Manager 50,000.00, Director 500,000.00) that refuses roles without a limit (Admin, unknown) and invoices over 500,000.00 for now, with reasons naming role, limit and what is needed; verify with `ApprovalPolicyTests` covering 50,000.00 / 50,000.01 / 500,000.00 / 500,000.01, Admin and an unknown role, asserting reason text contains the role and the limit
- [ ] 1.3 Change `ApprovalService.Approve(invoiceId, approver, role)` to run find -> ensure pending -> `ApprovalPolicy` -> mutate -> `Update`; verify with `ApprovalServiceTests` that a Manager approves 50,000.00 and a Director approves 500,000.00
- [ ] 1.4 Add "nothing changes" tests in `ApprovalServiceTests`: after a refused Manager approval of 50,000.01 assert the whole invoice state (Status, DecidedBy, DecidedAt, DecisionNote) equals its prior values and `FakeInvoiceRepository` recorded no `Update` (add an update counter to the fake if missing); verify the test fails if mutation is moved before the policy check
- [ ] 1.5 Map `ApprovalNotAllowedException` to `403 { error }` in `ApiControllerBase.Handle`; verify by calling the approve endpoint as `demo.manager` on a 60,000.00 invoice and getting 403 with the reason
- [ ] 1.6 Update roles: `ApprovalsController` approve requires `Manager`/`Director`, reject keeps `Manager`/`Director`/`Admin`, pass `session.Role` to the service; replace `demo.approver` in `appsettings.json` with `demo.manager`, `demo.director`, `demo.director2` (keep `demo.admin`); verify each demo user signs in and Admin gets 403 on approve
- [ ] 1.7 Confirm the web UI shows the refusal reason in `#decision-error` (existing `decide()` error path); verify manually by approving a 60,000.00 invoice as `demo.manager`

## 2. Two-approver rule (commit: `feat(approval): require two distinct approvers above 500,000`)

- [ ] 2.1 Add `ApprovalRecord(string Approver, string Role, DateTime ApprovedAt)` and `Invoice.Approvals` list (initialised empty, serialised in API responses); verify via a unit test and the invoices API JSON
- [ ] 2.2 Extend `ApprovalPolicy` for amounts over 500,000.00: first approval (Manager or Director) returns "record first approval"; second requires a Director whose trimmed, case-insensitive username differs from every recorded approver; verify with `ApprovalPolicyTests` for Manager-first allowed, Manager-second refused, same Director refused, `" Demo.Director "` vs `demo.director` refused
- [ ] 2.3 Update `ApprovalService.Approve` so a first approval appends a record and leaves `PendingApproval` with `DecidedBy`/`DecidedAt` null, and a valid second approval appends a record and sets `Approved`, `DecidedBy` = second approver, `DecidedAt`; verify with `ApprovalServiceTests` at 500,000.01 and 750,000.00
- [ ] 2.4 Add negative tests in `ApprovalServiceTests` for the same user twice (including different casing) and a Manager as second approver, asserting the whole invoice state (Status, DecidedBy, DecidedAt, DecisionNote, Approvals count and contents) is unchanged and no `Update` was recorded; verify each test fails if its rule is removed
- [ ] 2.5 Show "1 of 2 approvals" in the pending list in `wwwroot/app.js` when a pending invoice has exactly one recorded approval; verify manually after a first approval of a 750,000.00 invoice

## 3. End-to-end test (commit: `test(e2e): cover a refused over-limit approval in the UI`)

- [ ] 3.1 Add page objects under `e2e/pages/` (sign-in page and approvals page) using only role-based locators (`getByRole`, `getByLabel`); verify no CSS or XPath selectors are used
- [ ] 3.2 Add a Playwright test under `e2e/tests/` that signs in as `demo.manager`, creates and submits an invoice over 50,000.00, clicks Approve, and asserts the alert shows a reason containing "Manager" and "50,000" and the invoice is still pending; verify with `cd e2e && npm test`

## 4. Verification

- [ ] 4.1 Run Stryker (`cd backend/tests/InvoiceApp.Tests && dotnet stryker`) on the baseline before group 1 and after group 3; record both mutation scores and add tests for any surviving mutants in the policy or service
- [ ] 4.2 Run a PR review of the full diff using the `pr-review` format; answer every finding in the PR description with exactly one verdict (fixed / dismissed / deferred) and include the Stryker before/after scores
