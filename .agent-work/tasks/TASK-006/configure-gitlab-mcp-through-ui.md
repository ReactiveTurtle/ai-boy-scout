# TASK-006 - Configure Git hosting MCP through Todo Graph UI

## Goal

Let the user configure local stdio GitLab and GitHub MCP servers from the Todo Graph settings page.

## User decisions

- Configure local stdio MCP processes from the site.
- Keep separate read-only `gitlab` and write-capable `gitlab-write` configurations.
- Add a read-only `github` MCP configuration for GitHub PR comments; the user pushes and creates GitHub PRs manually.
- Let each project select GitLab or GitHub as its code host.
- GitHub is read-only for this workflow; the user creates and pushes GitHub PRs manually.
- Allow exact tool-name allowlists; no push tools may be enabled.

## Requirements

- Configure executable and argument list for each server.
- Store read/write tokens encrypted at rest; never return token values to the browser or write them in an OpenCode JSON file.
- Store the GitHub read token encrypted and never expose any provider token to the browser.
- Pass credentials to child MCP processes through environment variables.
- Show only whether a secret is configured; support replace/clear.
- Allow exact read/write MCP tool names in their respective agents; deny `push` tools and leave all write tools denied until explicitly allowed.
- Provide a short status/test action that does not expose credentials.

## Risks and compatibility

- The UI can launch local executables, so API remains loopback-only and arguments are treated as executable configuration, not shell text.
- Storage migrations must be additive; existing MCP profile files and settings remain usable.
- Use ASP.NET Data Protection for secret-at-rest encryption and a per-user key ring.

## Result and verification

- Added `/settings.html` to configure local stdio `gitlab` and `gitlab-write` commands, arguments, token environment names and exact tool allowlists.
- Tokens are Data-Protection encrypted in SQLite, never returned by GET, and passed to OpenCode subprocesses via environment variables. Generated OpenCode profiles contain only env references; generated files are deleted after each MCP agent process.
- Write allowlists reject push/branch/commit/merge tools; all write tools are denied by default until exact names are enabled.
- Store tests passed for token encryption and config generation; Edge browser check saved read/write settings and confirmed the API response contains no token.
- Actual configured GitLab MCP handshake is pending the user's MCP command and credentials.
