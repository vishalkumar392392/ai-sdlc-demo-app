# Design

## Context

See proposal.md (Why) and specs/invoice-approval/spec.md for the required behaviour.

Current state:
- `ApprovalService.Approve(invoiceId, approver)` finds the invoice, checks it is `PendingApproval`, then sets `Status = Approved`, `DecidedBy`, `DecidedAt` and calls `IInvoiceRepository.Update`. It knows nothing about roles or amounts.
- `Invoice` has `Id`, `CustomerName`, `Amount` (decimal), `Status`, `CreatedAt`, `DecidedBy`, `DecidedAt`, `DecisionNote`.
- `ApprovalsController` is `[RequireRole("Approver", "Admin")]` for both approve and reject and passes only the session username. `UserSession` already carries `Role`.
- `ApiControllerBase.Handle` maps `KeyNotFoundException` -> 404, `InvalidOperationException` -> 409, `ArgumentException` -> 400.
- Demo users live in `appsettings.json` (`demo.approver`/Approver, `demo.admin`/Admin).
- `wwwroot/app.js` renders the pending list and shows decision errors in `#decision-error` (`role="alert"`).
- `InMemoryInvoiceRepository` and `FakeInvoiceRepository` hand out live object references, so any mutation made before a throw is visible to later reads even without `Update`.
- `e2e/` has Playwright config but no tests or page objects yet.

Constraint: Domain must not reference ASP.NET or sessions; controllers must not compare amounts.

## Goals / Non-Goals

**Goals:**
- Keep all limit logic in one pure domain type that can be unit-tested exhaustively, including the boundaries.
- Guarantee by construction that a refused attempt never mutates the invoice (evaluate first, mutate after).
- Keep an audit trail of individual approvals.

**Non-Goals:**
- Configurable limits, per-department limits, or currency handling.
- Persisted storage or migration of real data (the repository is in-memory).
- Bulk or partial approval (UC-1, UC-2).
- Changing the decline (`Reject` with reason) flow beyond its role list.

## Decisions

1. **Limits are inclusive and compared as `decimal`.** Manager: `amount <= 50_000m`; Director: `amount <= 500_000m`; two approvers when `amount > 500_000m`. Rationale: "up to 50,000" reads naturally as including 50,000; `decimal` avoids binary floating-point surprises at 50,000.01. Alternative (exclusive limits) rejected as contrary to the plain wording.

2. **New roles Manager and Director; Approver removed; Admin cannot approve.** Demo users become `demo.manager` (Manager), `demo.director` (Director), `demo.director2` (Director), `demo.admin` (Admin). The approve action requires `Manager` or `Director`; the reject (decline) action keeps `Manager`, `Director`, `Admin`. The domain policy also refuses any role without a limit (Admin, unknown), so the rule holds even if the attribute is changed. Rationale: separation of duties (Admin manages and deletes; it does not spend), and defence in depth. Alternative (keep Approver as an alias for Manager) rejected: ambiguous and keeps a role the brief does not mention.

3. **Over 500,000: first approval from Manager or Director, second from a different Director.** The first approval appends an `ApprovalRecord` and leaves the invoice `PendingApproval` with `DecidedBy`/`DecidedAt` unset; the second appends a record and sets `Status = Approved`, `DecidedBy` = second approver, `DecidedAt` = now. Rationale: the brief only constrains the second approver, so the literal reading allows a Manager first. Alternative (require two Directors) recorded as an open question.

4. **Same-user comparison is trimmed and case-insensitive** (`string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)`). Rationale: `Demo.Director` and ` demo.director ` are the same account in practice; a strict `==` would let one person approve twice. The check runs against `Invoice.Approvals`, never `DecidedBy` (which is null while pending).

5. **`Invoice.Approvals`: `List<ApprovalRecord>` where `ApprovalRecord(string Approver, string Role, DateTime ApprovedAt)`.** Serves both audit and the distinct-approver check, and is serialised in API responses so the UI can show progress. Alternative (a single `FirstApprover` field) rejected: less auditable and does not generalise.

6. **`ApprovalPolicy` (pure) + `ApprovalNotAllowedException` + 403.** `ApprovalPolicy.EnsureCanApprove(invoice, approver, role)` returns the outcome (`ApproveFinal` or `RecordFirstApproval`) or throws `ApprovalNotAllowedException(reason)`; it reads but never writes. `ApiControllerBase.Handle` maps the exception to `403 { error }`. The reason names the role, the limit and what is needed, e.g. "Manager approval limit is 50,000.00; invoice INV-7 is 60,000.00 and needs a Director." Rationale: 403 is "authenticated but not allowed"; 409 stays for state conflicts (not pending). A dedicated exception keeps the domain free of HTTP. `ApprovalService.Approve(invoiceId, approver, role)` gains the role parameter; the controller passes `session.Role`.

7. **A refused attempt changes nothing.** Order inside `Approve`: find -> ensure pending -> `ApprovalPolicy.EnsureCanApprove` -> only then append record / set fields -> `Update`. No field changes and `Update` is not called on refusal. Rationale: repositories return live references, so validate-then-mutate is the only safe order. Tests assert the whole invoice state after the failed call and that the fake's update count is unchanged.

8. **UI.** The pending list shows "1 of 2 approvals" for a pending invoice that has exactly one recorded approval. The UI keys off `approvals.length`, not the amount, because only invoices over 500,000 can be pending with a recorded approval; no limits live in JavaScript. The refusal reason reaches `#decision-error` through the existing `decide()` error path, which already displays `error.message` from the `{ error }` body.

## Risks / Trade-offs

- [Existing users/tests sign in as `demo.approver`] -> Update `appsettings.json`, any tests and docs in the same commit as the role change; AGENTS.md already lists the new demo accounts.
- [Domain and attribute could disagree on who may approve] -> Policy is authoritative and unit-tested; the attribute is a coarse first gate only.
- [Same-user check bypassed by a different account owned by the same person] -> Out of scope; accounts are the unit of identity.
- [Live references leak partial mutation] -> Strict evaluate-then-mutate order plus whole-state assertions in tests; Stryker run to confirm the checks are killed by tests.
- [Limits hard-coded] -> Acceptable for the demo; centralised in `ApprovalPolicy` so a later change can make them configurable.

## Migration Plan

No data migration (in-memory store reseeds on start). Deploy as one release; rollback by reverting the change's commits. Users of `demo.approver` switch to `demo.manager` or `demo.director`.

## Open Questions

- Should a Manager be allowed to give the first of two approvals on invoices over 500,000? Chosen: yes, per the literal brief wording; revisit with the business. Changing it would only tighten one policy branch and its tests.
- Currency is not modelled; all amounts are assumed to be in one currency.
