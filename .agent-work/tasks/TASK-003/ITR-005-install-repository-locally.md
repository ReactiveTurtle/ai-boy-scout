# ITR-005 - Install MCP and site shortcut locally for a repository

State: verifying (human acceptance pending).

## Result

The site previews an OpenCode MCP registration and a shortcut to the common site
for an existing Git repository. Confirmation registers the project and writes
untracked local files ignored through Git's info/exclude, without copying the site.

## Primary risk

Filesystem setup and the SQLite project registration are not one atomic operation.
The installer refuses tracked/conflicting files but does not modify the global
OpenCode configuration. Restart OpenCode after installation; publish the MCP first.

## Verification

Repository installer tests and the Angular build passed.
