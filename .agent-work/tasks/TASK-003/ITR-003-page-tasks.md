# ITR-003 - Load tasks on demand

State: verifying (human acceptance pending).

## Result

The task API returns filtered cursor pages and the site loads more tasks only when requested. Note workflow requests no longer exceed the 100-ID API limit as the user loads more pages.

## Scope

Task paging and site loading; no Firebase query implementation or live synchronization.

## Verification

`dotnet test BoyScout.sln --no-restore` passed (100 tests); `npm --prefix web-ui run build` and `npm run test:mcp` passed.
