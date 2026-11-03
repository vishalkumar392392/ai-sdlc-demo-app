---
name: pr-review
description: Workshop review prompt for a pull request diff. Use when asked to review the current branch, a PR, or a diff. Produces findings in the fixed F<n> format defined in AGENTS.md so the author can give each one a verdict.
---

# PR review (workshop review prompt)

1. Get the diff: `git diff main...HEAD` (or `gh pr diff <n>`). Read AGENTS.md and the OpenSpec spec for the change first.
2. Review the diff as a sceptical senior engineer who did not write it. Check, in order:
   - **Correctness against the spec**: every requirement and scenario in the spec has code and a test. Boundaries (50,000 / 500,000), same-user rule, second-approver-is-Director rule, nothing-changes-on-rejection.
   - **Layering**: business rules only in Domain; controllers only translate HTTP.
   - **Tests that cannot fail**: assertions on exception type only, tests that never reach the rule, duplicated tests, missing negative cases.
   - **Security / authz**: role checks on endpoints, no trust in client-sent role or username.
   - **State safety**: any mutation before validation; shared references.
   - **UI / e2e**: error reason surfaced to the user; locators are role-based.
   - **Readability**: names, dead code, comments that lie.
3. Output ONLY findings, each in this exact format, numbered F1, F2, ...:

```
### F<n>: <short title>
- Severity: blocker | major | minor | nit
- Location: <file>:<line>
- Problem: <what is wrong and a concrete input that shows it>
- Suggested fix: <one or two sentences>
```

4. End with `Summary: <n> findings (<b> blocker, <m> major, <mi> minor, <ni> nit)`.
Do not praise, do not restate the diff, do not invent findings to fill space.
