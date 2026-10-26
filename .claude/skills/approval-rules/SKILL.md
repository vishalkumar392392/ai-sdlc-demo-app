---
name: approval-rules
description: Role-based invoice approval limits (Manager 50,000, Director 500,000, two distinct approvers above 500,000 with a Director second). Use whenever code, tests, specs or UI touch approving an invoice, ApprovalService, ApprovalPolicy, ApprovalsController or the Approvals tab.
---

# Approval rules

Source of truth: `openspec/changes/add-role-approval-limits/specs/invoice-approval/spec.md`. If this skill and the spec disagree, the spec wins; tell the user.

## Rules
1. Manager may approve invoices up to and including 50,000.
2. Director may approve invoices up to and including 500,000.
3. Over 500,000: two distinct approvers. First: Manager or Director. Second: Director.
4. Same user can never be both approvers (trimmed, case-insensitive username comparison).
5. Rejected attempt: throw `ApprovalNotAllowedException` with a reason naming role, limit and what is needed. Invoice is untouched and `IInvoiceRepository.Update` is not called.

## How to apply
- Put the limit table in `ApprovalPolicy` (Domain). No amounts in controllers or JavaScript.
- Order inside `ApprovalService.Approve`: find -> ensure pending -> evaluate policy (pure, no side effects) -> only then mutate and `Update`.
- Record each approval as an `ApprovalRecord(Approver, Role, ApprovedAt)` on the invoice so the second-approver check has data to compare against.
- `ApiControllerBase.Handle` maps `ApprovalNotAllowedException` to 403 `{ error }`.

## Traps an agent falls into
- Checking `invoice.DecidedBy` for the duplicate approver: it is null until the invoice is final. Check the recorded approvals.
- Comparing usernames with `==`: `Demo.Director` slips through.
- Setting `Status` or appending the approval before the checks, then throwing: the in-memory repository returns live references, so the change sticks.
- Using `>` vs `>=` inconsistently at 50,000 / 500,000. Limits are inclusive.
- Letting a Director's second approval pass when the first approver was the same Director.
