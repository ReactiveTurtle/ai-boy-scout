# ITR-018 - Request the remote repository URL only when creating an MR

**Type:** implementation  
**State:** verifying

## Topic

Keep project creation based on the local repository path; request the remote GitLab repository URL only after the user's push has been verified and the task is waiting for MR creation.

## Expected behavior

- Creating a project asks for its local Git repository path and code host, not a remote URL.
- After verified push, GitLab tasks without a saved remote URL wait for the user to provide it.
- Saving a valid remote URL resumes MR lookup/creation for waiting tasks in that project.
- Projects with a saved remote URL skip the prompt on later tasks.
- GitHub stays on the existing flow: the user creates the PR and attaches its PR URL.
- Invalid URLs leave tasks waiting and return a visible validation error.

## Verification

- Todo Graph tests cover the verified-push pause and resumption after saving the remote URL.
- Operational API check covers an empty project list, local-path project creation, URL request/save, and task association.
- Browser-driven form interaction remains unverified.
