# ITR-006 - Replace the rejected visual direction

Status: verifying

## Expected result

The application keeps the note-to-task workflow but adopts the calm, compact visual language shown in the user's reference.

## In scope

- rounded application shell on a pale lavender background;
- compact white header and left navigation;
- restrained green accent and subtle status colors;
- dense list rows with thin dividers;
- quiet AI proposal panel and readable details;
- responsive mobile layout.

## Out of scope

- backend and database behavior;
- new task fields or workflow states;
- copying the referenced product's brand or assets.

## Primary risk

Preserving too much of the rejected composition would retain its visual noise despite changing colors.

## Decision

Replace the page structure and component styling rather than reskinning the previous workshop layout.

## Topic evolution

The workshop/editorial direction from ITR-005 was rejected by the human as visually overwhelming. The supplied reference replaces that direction and becomes the source for density, hierarchy, spacing, and color restraint.

## Changed

- replaced the workshop composition with a rounded application shell;
- moved workspace and project navigation into a compact left sidebar;
- changed records to dense, divided list rows with quiet metadata columns;
- reduced the AI proposal to a pale-green review card;
- replaced the black, yellow, blue, and orange palette with white, gray, lavender, and restrained green;
- added compact mobile navigation and single-column records.

## Verification

- Angular production build passed with a 186.17 kB initial bundle;
- the live API and ready proposal loaded successfully;
- desktop and 500 px mobile screenshots were inspected against the supplied reference.

## Human confirmation

- [ ] The replacement visual direction is accepted
- [ ] Desktop density is accepted
- [ ] Mobile layout is accepted
