---
name: mutation-testing
description: Run Stryker.NET mutation testing on the Domain project and report the score in a fixed format. Use when asked for mutation scores, before opening a PR, or to judge test strength.
---

# Mutation testing

1. Install once (pinned, newer versions fail on .NET 8): `dotnet tool install -g dotnet-stryker --version 4.13.0`
2. Run: `cd backend/tests/InvoiceApp.Tests && dotnet stryker`
3. Copy `StrykerOutput/<timestamp>/reports/mutation-report.md` to `docs/mutation/<before|after>.md`.
4. Report in exactly this format:

```
Mutation score (<label>): <overall>%
| File | Score | Killed | Survived | No coverage |
| ... |
Surviving mutants worth killing: <file:line - mutation - why it survived>
```

5. For each surviving mutant in `ApprovalPolicy.cs` or `ApprovalService.cs`, either add a test that kills it or explain why it is equivalent.
