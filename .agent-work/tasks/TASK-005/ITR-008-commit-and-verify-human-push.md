# ITR-008 - Commit locally and verify a human push

**Type:** implementation  
**State:** ready

## Topic

After a successful build, prepare a local commit in the task worktree; instruct the user to push, then verify that the remote branch tip matches the committed SHA.

## Expected result

Only successful builds produce a local commit and `awaiting_push` state. No worker or agent calls `git push`. The user sees worktree path, branch and a suggested push command; background verification changes state to `pushed` only when the remote SHA matches.

## In scope

- Inspect status, diff, recent log; stage only intended, non-sensitive changes and verify the staged diff before committing.
- Persist committed SHA and branch; recover interrupted commit by inspecting the worktree rather than blindly repeating it.
- Expose manual push instructions and periodic remote verification with a bounded network timeout.
- Test with a temporary Git origin/checkout/worktree; verify that no remote ref appears until a test simulates the human push.

## Out of scope

- Creating the MR or automatically applying subsequent MR comments.

## Result and verification

- A successful build advances to a local-only Git commit in the task worktree after status, diff, last-ten-commits and staged-file checks. Sensitive-looking paths/diffs block automatic commit.
- Task enters `awaiting_push`; the UI shows the worktree, branch, command for the human and exact commit SHA. A background worker only calls `git ls-remote` and advances to `pushed` when the branch tip matches.
- Real temporary Git tests verified no remote ref appeared after automatic commit. A bare test remote imported the branch using `git fetch` to simulate a human push; the worker then observed the SHA. No `git push` was run by Todo Graph or the test agent.
