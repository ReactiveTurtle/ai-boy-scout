---
id: ITR-001
topic: Add the first Todo Graph route to the existing Angular SPA
type: implementation
state: accepted
---

## Expected result

`/todo-graph/projects` renders an Angular page with a shared Angular toolbar and project data from the Todo Graph API, while all other URLs continue to render the current Boy Scout UI.

## In scope

- Configure Angular Router with a Todo Graph route and a Boy Scout fallback route.
- Extract the existing Boy Scout root component without changing its behavior.
- Add a reusable standalone Angular toolbar and a typed Todo Graph projects API/page.
- Allow the configured local Angular origins to call the loopback Todo Graph API.
- Verify the Angular production build and the relevant route/API behavior where available.

## Out of scope

- Migrating project details, task/branch approval, graph, candidates, or settings screens.
- Resolving the observed duplicate upstream project ID; do not use numeric-ID-only project links as proof of correct project identity.
- Changing unrelated Boy Scout/Firebase code or working-tree changes.

## Verified facts

- `web-ui` is Angular 22 and builds to `src/BoyScout.Api/wwwroot`.
- Angular Router is already a dependency, but `app.config.ts` currently does not provide it.
- The existing root app is a large standalone component using `app.ts`, `app.html`, and `app.scss`.
- Todo Graph listens on `127.0.0.1:5207`; its API exposes `GET /api/projects/repositories`.
- Todo Graph currently has no CORS policy; Angular development runs on port 4200 and the built Boy Scout site uses port 5187.
- The current Todo Graph toolbar is a vanilla-JS custom DOM injection; the requested replacement must be an Angular component.

## Decision

Integrate Todo Graph under `/todo-graph` in the existing Angular SPA. Keep the Todo Graph API as a separate loopback service on port 5207 and allow requests only from the known local Angular origins (`localhost`/`127.0.0.1` on ports 4200 and 5187). Preserve legacy Boy Scout routes through a wildcard Angular route.

## Primary risk

Routing the existing monolithic Boy Scout component through a wildcard route could affect its current URL-driven views; verify the production build and smoke-test both a Todo Graph URL and an existing Boy Scout URL.

## Verification

- `npm run build` in `web-ui`: passed; Angular emitted the expected bundle. It reports the existing Boy Scout `app.scss` over its 26 kB component-style budget by 974 bytes.
- `dotnet build todo-graph/TodoGraph.csproj --no-restore -p:OutDir=<approved-temp-dir>`: passed with 0 warnings and 0 errors. The listener already using port 5207 was left untouched.
- `GET http://127.0.0.1:5187/todo-graph/projects`: HTTP 200 and Angular `index.html` fallback returned.
- `GET http://127.0.0.1:5207/api/projects/repositories`: returned 3 project rows.
- Isolated operational check using the newly built Todo Graph assembly on port 5217: Angular-origin CORS preflight returned 204 with `Access-Control-Allow-Origin: http://127.0.0.1:5187`; the projects endpoint returned 200 with that CORS header. The temporary process was stopped afterward.
- The live Todo Graph process on port 5207 (PID 33620) returned HTTP 405 to the CORS preflight with no allow-origin header, confirming it is still serving the pre-change build. It was not stopped.
- No Angular `*.spec.ts` files are present, so no UI unit tests were available.

## Operational follow-up

The live Todo Graph service on port 5207 must be restarted with the updated CORS policy before the Angular project list can load through that process. This runtime follow-up remains open; the user authorized continuing with the next implementation iteration.

## Topic checks

- Keep the first patch limited to the route shell and project landing page; do not migrate the project workspace or other Todo Graph screens in this iteration.
