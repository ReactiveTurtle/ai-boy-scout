# ITR-015 - Type workflow statuses as enums

**Type:** refactoring  
**State:** ready

## Topic

Replace stringly typed execution, build, MR, approval, mergeability, and code-host states with enums while keeping existing JSON/SQLite text values unchanged.

## Expected result

Compile-time state checks use enums; external status strings remain stable and old databases deserialize safely. Verify mapping round-trips, legacy fallback, and full tests.

## Boundary

- Do not rename persisted or UI string values.
- Preserve source task statuses as imported strings; enums apply to Todo Graph workflow state.
