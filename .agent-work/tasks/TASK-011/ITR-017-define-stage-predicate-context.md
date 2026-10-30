---
id: ITR-017
topic: Define the data context and evaluator for stage predicates
type: decision
state: proposed
---

## Expected result

Define which stage/run values predicates inspect and whether the application or AI evaluates them, before adding predicate behavior to the domain.

## User decision so far

The user selected predicate-based stage transitions.

## Open questions

- Are predicates evaluated by application code or interpreted by AI?
- Which values are available to a predicate: structured stage output, updated ProjectTask Title/Description, user response, or another explicit result model?
- What happens when no outgoing predicate matches or more than one matches?

## Out of scope until clarified

- Predicate syntax, evaluation, transition persistence, and inclusion in stage hashes.
