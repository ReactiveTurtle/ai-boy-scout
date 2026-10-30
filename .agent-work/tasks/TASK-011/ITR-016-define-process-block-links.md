---
id: ITR-016
topic: Define how ProcessBlocks connect within a Process
type: decision
state: accepted
---

## Expected result

Define the sequential ProcessBlock order and bounded stage-back-link behavior for the simple Process workflow.

## Verified requirement

The user clarified: a Process has ProcessBlocks containing stages, and those blocks connect to each other inside the Process. This supersedes the earlier Process-to-Process interpretation.

## Decisions already confirmed

- A ProjectTask starting a Process captures its full block graph at start.
- The latest user clarification is sequential execution without branching or parallelism; the earlier predicate-based routing choice is deferred.
- ProcessBlock order and stage order within each block define the forward path; there is no branching or parallelism.
- Backward links are only between stages in the same ProcessBlock and target an earlier stage.
- Each backward link has a repeat limit counted for that specific cycle. Exceeding it blocks the run with an error.

## Out of scope

- Implementing persistent runtime counters and the blocked/error state when a link limit is exceeded; this is a later execution iteration.
