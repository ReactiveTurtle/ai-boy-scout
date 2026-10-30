# ITR-007 - Implement and build in the task worktree

**Type:** implementation  
**State:** ready

## Topic

Run a restricted code-editing agent only in the approved worktree, automatically choose a relevant project build/check command and track the build separately from task phase.

## Expected result

On success, an approved task has source edits in its own worktree and a successful build, ready for a local commit. A failing build leads to diagnosis and another fix/build cycle without a fixed attempt count; if the correct fix is ambiguous or external dependencies block progress, pause visibly and ask the user.

## Primary risk

Automated code and build commands can change the user's repository. The coding agent must be confined to the task worktree and cannot run shell/Git commands; only the host executes the automatically detected build/check command. No push happens automatically.

## Boundary

- Never change or build in the source checkout; use only the worktree.
- Do not commit, push or create an MR in this iteration.
- No hardcoded 'success' if no reliable build/check can be detected.

## Result and verification

- Added a worktree-only OpenCode developer without shell/Git/MR tools; it can edit files and ask for clarification.
- Added project build/check detection for a single .NET solution/project, npm build/test script, or Go module, with an independent build state.
- A background worker cycles through diagnose/edit/build without a fixed retry count and pauses if the same build error recurs without progress.
- Targeted Todo Graph tests passed: 32/32, including a fake failing-then-passing build and build-command detection. Real build in a target project's worktree remains unverified.
