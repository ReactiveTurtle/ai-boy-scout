# ITR-014 - Show approvals and merge readiness

**Type:** implementation  
**State:** ready

## Topic

Normalize and display provider MR/PR approval and mergeability state separately from task phase.

## Expected result

Todo Graph shows request state, approvals (known count/requirement when provider exposes them), mergeability/check state, and a clear `Готово к слиянию` indicator only when the request is open, required approvals are satisfied, and the task build has passed. The user performs the merge.

## Boundary scenarios

- Open but awaiting approvals -> show awaiting; no ready indicator.
- Changes requested or merge conflicts/check failure -> show blocked/not ready.
- Provider does not expose required counts/status -> show unknown/not verified; do not infer ready.
- Closed/merged request -> stop comment polling and show provider state.
- Do not invoke merge tools or merge automatically.
