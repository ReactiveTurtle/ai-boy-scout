---
id: ITR-016
topic: Define what it means for one Process to connect to another
type: decision
state: proposed
---

## Expected result

Determine the endpoints, execution meaning, and snapshot boundary for the user-requested Process-to-Process connection before modelling it.

## Verified requirement

The user stated: “Один процесс может быть соединён с другим”.

## Open questions

- Is the connection from a Process to another Process on completion, or from an individual Stage to another Process?
- Does following a connection continue the same ProjectTask and the same execution?
- Must a run snapshot capture the whole reachable Process graph at start, or snapshot a connected Process when entered?
- Can a Process have multiple outgoing connections, and if so what selects one?

## Out of scope until clarified

- Adding link entities, evaluating cross-Process transitions, or changing snapshot contents.
