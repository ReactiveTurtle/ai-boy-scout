# ITR-002 - Create an empty local project

State: verifying (human acceptance pending).

## Result

The site can create a local project with an explicit name and existing directory path, without first adding a note. Duplicate paths are rejected; the next note for that path uses the same project.

## Scope

`ReportStore.CreateProject`, `POST /api/projects`, the sidebar form, and focused storage tests. No remote data or migrations.

## Verification

`dotnet test BoyScout.sln --no-restore` passed (100 tests); `npm --prefix web-ui run build` and `npm run test:mcp` passed.

## Next

Do not expose remote administrative credentials until the site's currently unauthenticated administrative API has a safe local-only boundary.
