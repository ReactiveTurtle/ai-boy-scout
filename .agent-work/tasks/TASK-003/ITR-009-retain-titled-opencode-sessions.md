# ITR-009 - Retain titled OpenCode sessions

State: verifying (human acceptance pending).

## Result

Background verification starts `opencode run` with a `BoyScout: verification
BSR-...` title. The session is not deleted after success, failure, timeout or
cancellation, and missing session IDs cannot invalidate a valid verdict.

## Verification

The local CLI exposes `run --title`. Generator tests and the 163-test Core
suite passed. The updated API was published and restarted.
