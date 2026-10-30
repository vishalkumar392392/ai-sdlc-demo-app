# Project conventions for AI agents

Invoice approval demo app: ASP.NET Core 8 Web API with a small static web UI.
Read this whole file before changing anything. It is short on purpose; every line is here because an agent got it wrong without it.

## Layout and layering

- `backend/src/InvoiceApp.Domain`: business rules, no web or storage code. `ApprovalService` owns the invoice lifecycle (Draft, PendingApproval, Approved, Rejected). `ApprovalPolicy` owns the role limits.
- `backend/src/InvoiceApp.Api`: controllers, sign-in (`Auth/`), in-memory repository, and the web UI under `wwwroot/`.
- `backend/tests/InvoiceApp.Tests`: xUnit tests, one test class per class under test, named `<ClassUnderTest>Tests`.
- `e2e`: Playwright end-to-end tests (`tests/`) and page objects (`pages/`).
- `docs/use-cases`: short briefs for features we may build.
- `openspec/`: spec-driven change proposals (OpenSpec). The spec for a change is the source of truth for its behaviour; update the spec first if behaviour must change.

Dependencies point one way: `Api -> Domain`. Domain never references ASP.NET, `HttpContext`, sessions or the repository implementation. Tests reference Domain only.

## Approval rules (business rules, owned by `InvoiceApp.Domain`)

| Invoice amount | Who can approve | Result |
| --- | --- | --- |
| up to and including 50,000 | one Manager or one Director | Approved |
| over 50,000 up to and including 500,000 | one Director | Approved |
| over 500,000 | two **distinct** approvers; the **second** must be a Director | first approval keeps it PendingApproval, second makes it Approved |

- Limits are inclusive: 50,000.00 is within a Manager's limit, 50,000.01 is not. Compare `decimal`s, never `double`.
- The first of two approvals may come from a Manager or a Director. The second must come from a Director.
- **The same user may never count as both approvers on one invoice.** Usernames are compared case-insensitively after trimming (`demo.director` and ` Demo.Director ` are the same person).
- Roles without an approval limit (`Admin`, anything unknown) cannot approve. Admin still deletes; approving is separation-of-duties.
- **A rejected attempt returns a clear reason and changes nothing about the invoice**: not `Status`, not `DecidedBy`, not `DecidedAt`, not the recorded approvals, and the repository `Update` is not called. Validate everything first, mutate after.
- Rejected attempts throw `ApprovalNotAllowedException` from the domain. `ApiControllerBase.Handle` maps it to **403** with `{ "error": "<reason>" }`. The reason names the role, the limit and what is needed instead.
- Declining an invoice (`Reject` with a reason) is a different operation from a rejected approval attempt. Do not confuse them.

## Conventions

- Controllers live in `Controllers/`, one per resource, route `api/[controller]`. Request bodies are `record` types declared inside the controller.
- Controllers inherit `ApiControllerBase` and use `Handle(...)` so service errors map to 404, 409, 400 and 403.
- Every endpoint is behind sign-in. Restrict actions with `[RequireRole("...")]`: destructive actions are Admin only, approvals are Manager or Director.
- Business rules go in the Domain project, never in controllers. Controllers pass the signed-in username and role through; they do not compare amounts.
- Register services in `Program.cs`. Repositories are interfaces in Domain with an in-memory implementation in Api.
- Every new rule or endpoint needs tests. Use `FakeInvoiceRepository` in tests.
- Use clear names; no one-letter variables or `flag` parameters.
- End-to-end tests use page objects and role-based locators (`getByRole`, `getByLabel`), never CSS or XPath selectors.

## Commands

- Run the app: `dotnet run --project backend/src/InvoiceApp.Api` then open http://localhost:5000
- Run the tests: `dotnet test backend/InvoiceApp.sln`
- Run end-to-end tests: `cd e2e && npm install && npx playwright install chromium && npm test`
- Run mutation tests: `cd backend/tests/InvoiceApp.Tests && dotnet stryker` (config in `stryker-config.json`; install with `dotnet tool install -g dotnet-stryker --version 4.13.0`, newer versions do not install on .NET 8)

Run the unit tests after every change. Run Stryker before opening a pull request and paste the score in the PR.

## Testing expectations

- Every approval rule has at least one test that passes **and** one that would fail if the rule were broken. Boundary values are mandatory: 50,000.00, 50,000.01, 500,000.00, 500,000.01.
- The two negative rules (same user twice; rejected attempt changes nothing) are tested by asserting on the **whole** invoice state after the failed call, not only on the exception type.
- Assert the reason text contains the role and the limit, so a vague message fails the test.
- A test that cannot fail is worse than no test. Before keeping a generated test, ask: which line of production code would have to break for this to go red?

## Review expectations

When asked to review a diff (see the `pr-review` skill), report each finding as:

```
### F<n>: <short title>
- Severity: blocker | major | minor | nit
- Location: <file>:<line>
- Problem: <what is wrong and the concrete input that shows it>
- Suggested fix: <one or two sentences>
```

The author answers every finding in the PR description with exactly one verdict: **fixed** (commit), **dismissed** (reason), or **deferred** (reason and where it is tracked).

## Commits

Conventional Commits: `<type>(<scope>): <what changed>`, a blank line, then a body that says **why**. One logical step per commit; every commit builds and passes tests.

## Demo accounts (not real credentials)

- `demo.manager` / `ChangeMe123!` (role Manager, limit 50,000)
- `demo.director` / `ChangeMe123!` (role Director, limit 500,000)
- `demo.director2` / `ChangeMe123!` (role Director, used as the second approver)
- `demo.admin` / `ChangeMe123!` (role Admin, cannot approve)

## Never

- Commit real credentials, tokens or customer data.
- Add business logic to `Program.cs` or controllers.
- Mutate an invoice before every approval check has passed.
- Weaken or delete a failing test to make a change pass.
