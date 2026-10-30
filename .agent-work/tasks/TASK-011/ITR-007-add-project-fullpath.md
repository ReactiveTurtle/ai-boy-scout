---
id: ITR-007
topic: Add the required Project.FullPath property
type: implementation
state: verifying
---

## Expected result

The `Project` aggregate contains a required `Name` and absolute `FullPath`, while keeping its internal GUID identity stable.

## Project principle

- A TodoGraph `Project` represents one local workspace.
- `Id` is an internal GUID; neither name nor path is the entity identity.
- `Name` is required and trimmed. `FullPath` is required and absolute; the entity stores the supplied path without canonicalization.
- Domain validation does not inspect the filesystem or require the directory to exist.

## Scenarios

1. Creating a project with a valid name and absolute path stores the trimmed name and supplied full path.
2. Creating a project with a relative, empty, or whitespace-only path is rejected.
3. Empty or whitespace-only project names are rejected.

## In scope

- Add `FullPath` to the `Project` entity and require it in the factory.
- Validate that the supplied path is absolute without filesystem access.

## Out of scope

- Verifying that the directory exists, Git repository detection, uniqueness across projects, and path changes after creation.
- Persistence or API behavior.

## Primary risk

Absolute path semantics vary by operating system; use `Path.IsPathFullyQualified` for the current platform and preserve the supplied path.

## Topic evolution

- The initial implementation added path canonicalization, a `Rename` operation, and nine small tests. The user called out YAGNI; those extras were removed. The current scope is the requested fields, stable identity, and minimal required-field validation.

## Verification

- Full solution build to temporary output.

## Verification decision

- Per user YAGNI feedback, removed the scaffold-only Project unit-test project and did not replace it with more tests. Validation for this iteration is a full solution build; add tests when a non-trivial domain behavior warrants them.

## Verification result

- Full solution build to temporary output passed with 0 warnings/errors after removing the extra behavior and scaffold-only tests.
