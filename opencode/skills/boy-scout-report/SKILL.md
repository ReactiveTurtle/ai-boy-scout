---
name: boy-scout-report
description: Boy Scout Report captures verified issues discovered outside the current task scope through MCP. Use when incidental technical debt, a separate defect, missing tests, or maintainability risk is found during coding, investigation, or review.
---

# Boy Scout Report

Preserve useful adjacent discoveries without expanding the current task.

## When to record

Create a report when all of these are true:

1. The issue was observed in code, behavior, tests, or documentation.
2. It is actionable and relevant to the project.
3. Fixing it is not required for the current task result.
4. It is not merely a preference or an unverified suspicion.

Do not create reports for the requested work itself, trivial style preferences, or speculative redesigns.

## Workflow

1. Keep the current patch within its stated scope.
2. Use `boy_scout_list` when necessary to avoid an obvious duplicate.
3. Record the discovery with `boy_scout_add`.
4. Mention the new report ID to the human.
5. Do not fix the report unless the human explicitly puts it into scope.

Use `location` for the most precise known file, symbol, or component. Write `observation` as a verified fact, `impact` as the practical consequence, and `suggested_action` as a possible follow-up rather than a command.

Only a human moves a report to `accepted`, `planned`, or `dismissed`. Set `resolved` only after the corresponding change has been verified.

The report is a concise decision record, not hidden chain-of-thought or a transcript of the investigation.
