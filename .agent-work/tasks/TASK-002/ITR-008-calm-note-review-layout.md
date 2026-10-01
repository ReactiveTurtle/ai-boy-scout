# ITR-008 - Calm the note review layout

Status: verifying

## Expected result

The note list is easy to scan and the selected note reads as a distinct workspace instead of another table column.

## In scope

- compact warning with expandable evidence;
- simplified list rows and stronger selected state;
- fixed-width, visually separated detail panel with metadata and actions;
- project search, basename labels, and collapsed low-volume projects;
- status tabs with counts;
- focus mode and collapsible navigation.

## Out of scope

- new task workflow states;
- arbitrary filesystem access;
- redesigning the database project identity model.

## Primary risk

Hiding secondary information could make technical evidence harder to reach.

## Decision

Keep evidence available through details and copy/open actions while reducing its default visual weight.

## Verification evidence

- Angular production build passed;
- desktop and 500 px mobile screenshots reviewed;
- rejected-note details expose a persisted reason or `Причина не зафиксирована` for legacy data;
- explicit project access was split into ITR-009 after the permission model was clarified.
