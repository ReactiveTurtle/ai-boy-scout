# TASK-002 - Turn raw notes into implementation-ready tasks

## Goal

Keep Boy Scout Reports as raw notes and use OpenCode in the background to prepare task drafts that a human confirms before they become implementation tasks.

## Success criteria

- the interface and product terminology are Russian;
- notes and tasks are separate entities;
- OpenCode prepares task drafts without modifying source code;
- a task is created only after human confirmation;
- confirming a draft marks its source note as processed;
- task status is either `todo` or `done`;
- the interface has a distinctive, non-template visual language.

## Constraints

- preserve the existing four MCP tools and their status contract;
- preserve all existing report IDs and rows;
- keep SQLite as the local source of truth;
- AI failures must not create tasks or mark notes as processed.

## Known risks

- uncontrolled background processing could create excessive OpenCode sessions;
- untrusted note text must not turn into executable agent instructions;
- a partial confirmation could desynchronize notes and tasks.

## Excluded

- automatic task creation without human confirmation;
- task assignment, deadlines, priorities, and remote synchronization;
- modifying project source code during note analysis.
