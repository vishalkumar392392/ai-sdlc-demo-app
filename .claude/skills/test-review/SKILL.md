---
name: test-review
description: Review generated unit or e2e tests for whether they can actually fail. Use after an agent writes or changes tests, before committing them.
---

# Test review

For every test in the diff, answer in a table:

| Test | Rule covered | What production change would make it fail? | Verdict (keep / strengthen / delete) |

Rules:
- If you cannot name a concrete production change that turns the test red, the verdict is *strengthen* or *delete*.
- Negative-rule tests must assert the full invoice state after the failed call (Status, DecidedBy, DecidedAt, Approvals count) and that `Update` was not called.
- Boundary tests must exist on both sides of each limit.
- Reason-text tests must check role and limit appear in the message.
- Prove it: temporarily break the rule (e.g. change `<=` to `<`), run `dotnet test backend/InvoiceApp.sln`, confirm a test fails, revert. Record which test caught it.
