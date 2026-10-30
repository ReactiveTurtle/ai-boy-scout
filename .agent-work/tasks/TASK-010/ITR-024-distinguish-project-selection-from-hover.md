---
id: ITR-024
topic: Distinguish the selected TodoGraph project from hovered projects
type: implementation
state: verifying
---

## Expected result

The project used by the local-path inspector has one clear selected style; hovering another project does not make it appear selected.

## Scenarios

1. Choosing a project for path binding marks only that project as selected in the sidebar and main list, and shows its name in the inspector.
2. Hovering a different project leaves selection on the original project and uses a visibly different hover treatment.
3. Choosing another project transfers the selected treatment to it; the previous project is no longer selected.
4. Cancelling clears the selected treatment and hides the binding form.

## In scope

- Make hover and selected visual states distinct in the TodoGraph projects page.
- Preserve the existing single `selectedId` behavior.

## Out of scope

- Project identity, route navigation, filtering, or local-path binding behavior.

## Primary risk

The current combined `:hover,.selected` rules make two visually different states look identical, even though the script stores only one selected project ID.

## Verification

- Headless browser check of project selection transfer, hover styling, and cancellation with mocked project API data.

## Verification result

- Playwright against a temporary static server with mocked projects: selecting `sample-project` produced exactly one selected card and sidebar entry; hovering `DiscordCutieBot` did not alter selection and had a different background; selecting it transferred selection; cancel cleared selection and hid the binding form.
- Observed colors distinguish the states: selected card `rgb(241, 248, 241)` vs hovered card `rgb(251, 252, 251)`; selected sidebar `rgb(239, 246, 239)` vs hovered sidebar `rgb(247, 249, 247)`.
- Root cause confirmed: `selectedId` was already a single value; the CSS had combined `:hover` and `.selected` using the same visual style.
