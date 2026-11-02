# TodoGraph WebApp Frontend Rules

## Architecture

- Use the latest stable Angular version compatible with the repository. Prefer standalone components and the repository's current Angular conventions.
- Structure the UI with the requested MVC/MVVM separation:
  - The page Controller lives at the root of the page folder (`<page>.page.ts`).
  - The page View lives in `<page>.page.html` and may bind only to its ViewModel; templates must not read or mutate Model directly.
  - The ViewModel and Model live in the page's `entities/` folder. The ViewModel contains/wraps the Model and exposes presentation-ready state and actions.
  - Controllers coordinate the ViewModel and interface-based services; keep API/data access out of templates and presentation-only components.
- Each page has its own folder under `src/app/pages/`, named `<page-name>-page/`. Example:

  ```text
  src/app/pages/process-page/
    process.page.ts
    process.page.html
    process.page.scss
    entities/
      process.model.ts
      process.view-model.ts
    shared/
      services/
        process.service.interface.ts
        process.service.ts
      components/
        undo.components.ts
        undo.components.html
        undo.components.scss
  ```

- `src/app/core/components/` is the app-wide component library for foundational UI primitives and shared design-system behavior.
- `src/app/shared/` is for cross-page shared features/components/services that are not foundational core primitives.
- `<page>/shared/` is for components and services reused only inside that page. Keep page-specific entities and behavior out of global shared folders.
- Every service must have a corresponding interface. Because TypeScript interfaces are erased at runtime, provide an Angular `InjectionToken` when injecting an interface.
- Keep a component's TypeScript, template, and stylesheet together with the same basename. For component files use the requested form, for example `undo.components.ts`, `undo.components.html`, and `undo.components.scss`.
- Keep components focused on presentation. Prefer Angular's official feature-oriented organization and one concept per file while following this repository's explicit page/shared/core folder rules.

## When to generalize a component

Use these criteria before moving a page component into `shared` or `core/components`:

1. There are concrete current consumers, not merely possible future uses. One use stays local; a second real use is a signal to evaluate sharing, not an automatic extraction rule.
2. Consumers share the same behavior and semantics, not just similar markup, colors, or layout.
3. The component has a small, stable public contract (inputs/outputs) and does not depend on a specific page, route, or page Model.
4. The abstraction removes meaningful duplicate behavior or consistency work and makes future changes easier overall.
5. Consumers can express their genuine differences through composition or a few meaningful inputs; a growing set of flags, conditionals, or page-specific exceptions is a signal to keep separate components.
6. Cross-page foundational UI belongs in `core/components`; cross-page feature-level reuse belongs in root `shared`; single-page reuse belongs in that page's `shared` folder.
7. If consumers evolve differently, prefer separate page components over a misleading shared abstraction.

There is no fixed occurrence-count rule. Prefer the smallest clear design and defer speculative abstractions until actual use cases reveal a stable common contract.

## References

- Angular Style Guide (v22): https://angular.dev/style-guide
- Angular component anatomy and separate template/style files: https://angular.dev/guide/components
- Martin Fowler, YAGNI: https://martinfowler.com/bliki/Yagni.html
- Kent C. Dodds, AHA Programming: https://kentcdodds.com/blog/aha-programming
