# ITR-011 - Restore MCP after a schema mismatch

State: verifying (human acceptance pending).

## Finding

The global OpenCode MCP entry pointed to `artifacts/mcp/BoyScout.Mcp.dll`.
That published copy supported SQLite only through schema v4, but the working
database had schema v9. The process exited before its stdio handshake.

## Result

Created a SQLite backup, published the current MCP, then opened the working
database with schema v11. The old published binary was not used again.

## Verification

The published MCP smoke test passed. `opencode mcp list` reports `connected`.
`verify:migration` found 178 immutable reports unchanged, no orphan reports,
and no foreign-key errors. A previously running OpenCode client needs restart
to reconnect its already-closed MCP session.
