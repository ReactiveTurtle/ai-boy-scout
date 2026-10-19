# ITR-038 - Enter a project workspace

## Topic

Make project selection the top navigation level; Notes, Tasks, and Firebase storage settings belong to the entered project.

## Expected result

Opening Boy Scout shows a project chooser. Selecting a project enters its workspace; a clear project-level action returns to the chooser. Existing project shortcuts continue opening the matching workspace directly.

## In scope

- add a project landing/chooser screen;
- enter/exit project workspace and scope Notes/Tasks to the selected project;
- move local project creation into the chooser;
- preserve project storage/remote panels inside the selected project's settings;
- preserve `projectPath` shortcut behavior.

## Out of scope

- changing project or storage backend contracts;
- replacing existing Notes/Tasks filtering/pagination;
- changing Firebase activation or workflow semantics.

## Primary risk

Do not load an unscoped Notes/Tasks workspace before a project is entered, and keep shortcut-selected projects scoped to the matching local project.

## Verification

`npm --prefix web-ui run build` succeeded without budget warnings. `dotnet test BoyScout.sln` passed (293 tests). The UI has not yet been published/restarted for a live navigation smoke check.

## Result

The default route now opens a project chooser with search, project creation and an all-projects entry. Entering a project scopes Notes/Tasks and changes the URL; an in-app project switcher returns to the chooser. The project query/shortcut still opens the matching project directly, including a requested Notes or Tasks workspace.

The README describes the chooser/enter/back flow.

## State

verifying; human acceptance pending
