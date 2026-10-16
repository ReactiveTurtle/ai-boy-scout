# ITR-016 - Carry comments into the next agent check

State: verifying (human acceptance pending).

## Result

Returning a processing, rejected, or completed note to the queue accepts an
optional comment of at most 2,000 characters. It is saved as the proposal's
requested clarification and passed to the next read-only OpenCode verification.
An empty processing requeue preserves an earlier clarification. The site shows
the optional comment fields and keeps entered text after a failed request.

## Verification

Core tests passed and the new site bundle built. The published API accepts
empty-body POSTs on all three routes (404/409 for nonexistent IDs rather than
415); production data was not changed during this HTTP check.
