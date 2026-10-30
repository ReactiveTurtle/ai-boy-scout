---
id: ITR-011
topic: Decide whether candidate intake and task delivery share one Process instance
type: decision
state: accepted
---

## Expected result

Choose the Process aggregate lifecycle boundary before modeling EF relationships or deriving Candidate/Task status from stage state.

## Verified context

- User raw submissions and BoyScout candidates begin as Candidate records.
- A TaskNode is materialized only after questions and acceptance criteria are complete.
- Existing BoyScout task snapshots can enter TodoGraph as tasks without a new raw candidate.
- The process stages must cover both user/agent clarification loops and task implementation gates.

## Human direction and decision

The user said the lifecycle begins with the raw task. Model one end-to-end Process aggregate per raw request: it starts with the Candidate/raw item, loops through agent/user clarification, creates and links a TaskNode only after criteria are complete, then continues through branch approval and implementation stages. Candidate and Task remain separate domain entities; Process references their IDs and owns orchestration state.

Already-structured imported BoyScout tasks may enter the same built-in Process at the appropriate task stage to preserve the existing integration.

## Primary risk

The process lifecycle spans Candidate and Task entities; enforce that TaskNode is not linked before acceptance criteria are established.

## Verification

No production code changes were made in this decision iteration. Existing store characterization passed 64/64.
