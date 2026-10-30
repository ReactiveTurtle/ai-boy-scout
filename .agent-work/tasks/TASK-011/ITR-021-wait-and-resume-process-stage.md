---
id: ITR-021
topic: Pause for user input and resume the same ProcessStage
type: implementation
state: verifying
---

## Expected result

A ProcessStage can pause a ProcessRun with a prompt, accept non-blank free-text input, and resume that same stage without creating another ProcessRun.

## Scenarios

1. A stage result with a prompt may omit Title and Description; it sets `WaitingForUser` and leaves the current stage unchanged.
2. A waiting stage result may also update Title and/or Description atomically before pausing.
3. Submitting non-blank free text stores the response, clears the active prompt, and resumes the same stage in `Running` state.
4. The next result from that stage consumes the pending response; it may wait again or complete with a required Title/Description update.
5. Blank input, blank prompts, or submission when not waiting are rejected without mutating the task or run.
6. Wait/resume does not create another ProcessRun or alter the captured Process snapshot.

## User decision

The user chose prompt + free-text response and resuming the same stage.

## In scope

- Add waiting prompt, pending response, `WaitingForUser` status, and same-stage resume to ProcessRun.
- Preserve the existing one-ProcessRun-per-ProjectTask rule.

## Out of scope

- UI/API delivery of the prompt and response, persistent runtime state, and AI invocation.

## Verification

- Build the TodoGraph solution.
- Run an isolated domain smoke for prompt, wait, response, same-stage resume, invalid input, and task-field atomicity.

## Verification result

- Added a `WaitingForUser` ProcessRun state, current prompt, pending free-text response, and `ProjectTask.SubmitProcessUserResponse`.
- A prompt result may omit Title/Description or update them atomically; submitting non-blank text resumes the same stage and retains the answer for the next stage result.
- Invalid/blank prompts or responses leave task fields, stage position, prompt, and status unchanged.
- Release solution build passed with 0 warnings and 0 errors.
- A temporary domain smoke passed for pause, repeated wait, response, same-stage resume, completion, and invalid-input atomicity; its source was removed.
- Wait state is not persisted or exposed through API/UI; the operational end-to-end user interaction is not yet implemented.
