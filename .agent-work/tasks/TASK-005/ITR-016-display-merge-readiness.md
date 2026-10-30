# ITR-016 - Display approvals and merge readiness

**Type:** implementation  
**State:** verifying  

## Topic

Show provider approvals/mergeability separately and label a request ready to merge only when it is open, approvals are satisfied, provider reports mergeable, and the latest local build passed.

## Boundary

- Never perform a merge automatically.
- When provider data is missing or unknown, never infer readiness.
- Display approver counts when known; otherwise show provider state/unknown.

## Patch and verification

- The inspector now shows approval state, known approved/required counts, provider mergeability, latest build status, and a readiness label for linked MR/PR nodes.
- `Готов к слиянию` appears only for an open request with approvals satisfied, provider mergeable, and build status `passed`.
- `node --check todo-graph/wwwroot/graph.js` passed.
- Full solution tests passed: 339/339, using a temporary output directory because the existing Todo Graph process locks the normal output DLL.
- Browser rendering was not exercised in this iteration.
