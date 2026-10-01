# ITR-006 - Browse reports in a web UI

Status: awaiting acceptance

## Expected result

The user can open a local responsive web UI, browse current Boy Scout Reports, filter by project and status, search their text, and inspect full report details.

## In scope

- paginated read queries for projects and reports;
- local ASP.NET Core API;
- Angular report browser;
- project, status, and text filters;
- report selection and details;
- desktop and mobile layouts;
- production build and local startup commands.

## Out of scope

- editing reports or statuses;
- categories and themes;
- task creation and report-task links;
- AI suggestions;
- authentication or remote deployment.

## Primary risk

The UI could load an incomplete or misleading subset of reports while presenting it as the complete backlog.

## Decision

Use cursor pagination from the API, expose totals explicitly, and keep the first UI read-only.

## Changed

- added project summaries and paginated report browsing to `BoyScout.Core`;
- added a local ASP.NET Core API for health, projects, report pages, and report details;
- added a responsive Angular report browser with project, status, and text filters;
- added read-only report selection, details, totals, and cursor-based load-more behavior;
- added production publish/start commands with Angular hosted by ASP.NET Core.

## Behavioral result

The published application serves the report browser and API from `http://127.0.0.1:5187`. Filters restart pagination, displayed totals come from the complete filtered query, and additional pages append without presenting the first page as the whole backlog.

## Verification

- Angular production build passed with a 168.59 kB initial bundle;
- `dotnet test BoyScout.sln --configuration Release` passed all 6 tests;
- `npm run publish:ui` produced the combined application in `artifacts/api`;
- `/api/health`, `/api/projects`, filtered `/api/reports`, and `/api/reports/{id}` passed against the working database;
- the production root returned the Angular application with HTTP 200;
- runtime verification found 13 projects and 128 open reports, with five returned in the requested first page.

## Not checked

- visual behavior and responsive layout in an interactive browser;
- accessibility audit and end-to-end browser automation.

## Human confirmation

- [ ] Report browsing and details are accepted
- [ ] Desktop and mobile layouts are accepted
- [ ] The read-only scope is accepted
