---
id: ITR-017
topic: Define the data context and evaluator for stage predicates
type: decision
state: abandoned
---

## Expected result

Define which stage/run values predicates inspect and whether the application or AI evaluates them, before adding predicate behavior to the domain.

## Initial user decision

The user selected predicate-based transitions evaluated by application code against structured stage results.

## Topic evolution

The user later clarified: “Без параллельности и ветвлений”. Predicate-driven routing is deferred for the requested simple sequential version; the stage result schema for routing is not needed in this scope.

## Superseded scope

- Predicate schema, evaluation, transition persistence, and inclusion in stage hashes are not part of the current sequential model.
