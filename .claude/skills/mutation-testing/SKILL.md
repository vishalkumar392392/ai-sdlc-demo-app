---
name: mutation-testing
description: Run Stryker.NET mutation testing on the Domain project and report the score in a fixed format. Use when asked for mutation scores, before opening a PR, or to judge test strength.
---

# Mutation testing

1. Install once (pinned, newer versions fail on .NET 8): `dotnet tool install -g dotnet-stryker --version 4.13.0`
2. Run: `cd backend/tests/InvoiceApp.Tests && dotnet stryker`
3. The `cleartext` reporter prints the per-file score table in the console; copy it into `docs/mutation/<before|after>.md`. The full report is `StrykerOutput/<timestamp>/reports/mutation-report.html`. (Do not enable the `markdown` reporter: Stryker 4.13 crashes printing its path when the repo path contains a space.)
4. Report in exactly this format:

```
Mutation score (<label>): <overall>%
| File | Score | Killed | Survived | No coverage |
| ... |
Surviving mutants worth killing: <file:line - mutation - why it survived>
```

5. For each surviving mutant in `ApprovalPolicy.cs` or `ApprovalService.cs`, either add a test that kills it or explain why it is equivalent.
