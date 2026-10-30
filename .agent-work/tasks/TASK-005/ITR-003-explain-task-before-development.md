# ITR-003 - Explain task before development

**Type:** implementation  
**State:** ready

## Topic

Consume queued tasks with a read-only analysis agent and present a plain-language explanation of the problem, affected behavior, risks, corner cases and any genuine requirements questions before code changes.

## Expected result

A queued task moves to `clarifying` and persists a Russian explanation visible on the task page; if the analysis fails, the task becomes `failed` with an error rather than remaining silently queued. The user can read the explanation and answer genuine questions in the UI before authorizing implementation.

## Scope

- Run analysis from the configured project checkout in a separate OpenCode profile with only read-only repository tools.
- Persist a normalized explanation/risks/corner cases/questions/decomposition suggestion.
- Surface the result in the graph inspector as readable HTML and allow human answers to questions.
- Ensure a queued task cannot be claimed twice; rejection before claim prevents analysis.

## Boundary

- Do not edit source code, create worktrees, build, commit, push, or create an MR in this iteration.
- Suggestions for decomposition do not create child tasks without explicit user approval.
- No claim of development progress until a separate implementation worker exists.

## Verification

- Added a durable preflight record and a read-only OpenCode agent in the configured Git checkout; the worker atomically claims a queued task and stores a structured explanation or a visible failure.
- Added HTML rendering of problem, expected behavior, evidence, risks, corner cases, questions and optional decomposition, with a field to save clarifications.
- Targeted Todo Graph tests passed: 26/26, including single-claim processing, rejection, startup recovery, persistence of answers and strict parsing; JS/config syntax checks passed.
- Isolated runtime check: queued task was claimed, unavailable analyzer surfaced `failed` plus error, and the preflight API returned `{preflight:null}` (fixed during verification; it previously returned an empty body that the UI could not parse).
- Real OpenCode analysis of a repository remains unverified; the running user service still uses its old assembly.
- A live OpenCode run initially failed because the Russian-text validator also rejected technical path evidence. The validator now requires Russian for explanatory text but permits file/path evidence; the live scenario is being rerun.
- The rerun passed against the local repository: it rendered a Russian explanation, listed risks/questions, detected `dotnet test ...BoyScout.sln`, and detected base branch `master`.
- A second live run verified the clarification cycle: the user answer was persisted, OpenCode re-ran the analysis and returned the revised report; development remained paused pending remaining questions.
