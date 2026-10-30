---
id: ITR-009
topic: Create a standalone TodoGraph.WebApp Angular scaffold
type: implementation
state: verifying
---

## Expected result

`src/TodoGraph.WebApp` is an independent Angular application scaffold generated with the latest Angular CLI/Core available from npm, separate from BoyScout's `web-ui`.

## In scope

- Generate the standalone Angular application under `src/TodoGraph.WebApp`.
- Use the latest compatible Angular packages and lock resolved versions.
- Skip generated test files and keep the app feature-empty.

## Out of scope

- API/SignalR integration, routes, project screens, styling work, and generated tests.
- Changes to the existing BoyScout Angular `web-ui`.

## Primary risk

The latest Angular release may require a newer Node.js version than the repository currently uses.

## Verification

- Production Angular build.
- Confirm locked Angular CLI/Core versions and absence of generated test files.

## Verification result

- Generated the standalone app under `src/TodoGraph.WebApp` with Angular CLI 22.2.5 and `--skip-tests`.
- `npm install`: completed, 0 vulnerabilities; lockfile resolves Angular CLI/build 22.2.5 and Angular Core/Common/Compiler/Compiler CLI/Forms/Router/Platform Browser 22.2.4.
- `npm run build`: passed; initial bundle is 217.10 kB.
- No `.spec.ts` files were generated. No TodoGraph API/SignalR behavior is wired into this starter yet.
