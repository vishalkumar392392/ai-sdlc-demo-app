---
name: step-commit
description: Commit one implementation step with a Conventional Commit message that says what changed and why. Use whenever a step of a change is complete and tests pass.
---

# Step commit

1. `dotnet test backend/InvoiceApp.sln` must pass. Do not commit red.
2. Stage only the files for this step (`git add <paths>`), never `git add -A` blindly.
3. Message format (enforced by the `validate-commit` PreToolUse hook):

```
<type>(<scope>): <what changed, imperative, <= 72 chars>

Why: <the reason this step exists, linked to the spec requirement>
<optional details / test notes>

Co-Authored-By: <agent> <noreply@...>
```

Types: feat, fix, test, docs, chore, refactor. One logical step per commit.
