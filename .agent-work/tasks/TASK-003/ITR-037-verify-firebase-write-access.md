# ITR-037 - Verify Firebase write access

## Topic

Verify that the service account can perform the same root write used by Firebase note operations without changing project data.

## Expected result

Activation preflight performs an ETag-protected PUT of the exact current project JSON. It reports ready only when conditional writes are accepted; a denied/contended probe blocks confirmation without changing notes/tasks.

## In scope

- add a bounded no-change ETag write probe to Firebase project repository;
- include write access in preflight readiness, token and UI checklist;
- test successful no-op, ETag conflict retry and no content change;
- preserve retryable Firebase connections and avoid local fallback.

## Out of scope

- writing/deleting probe records or schema markers;
- actual note creation or project activation;
- modifying Firebase security rules.

## Primary risk

Do not create persistent probe data or overwrite concurrent root updates; send the exact read JSON with its ETag and retry only precondition conflicts.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseReportRepositoryTests` passed (17 tests). `dotnet test BoyScout.sln` passed (291 tests). The Angular build succeeded without budget warnings. `git diff --check` passed.

## Result

`FirebaseReportRepository.VerifyProjectWriteAccessAsync` reads the bound project root and conditionally PUTs the exact same JSON with the returned ETag. It retries only precondition conflicts and reports false after repeated contention; no content/node is created or changed. Activation preview now includes this check in readiness and confirmation-token data, so read-only Firebase credentials cannot enable note writes. The project panel displays the write-access result.

## Residual risk

The write probe was verified with a fake Firebase HTTP handler; no live Firebase account was available. Production permissions and listener behavior for an identical-root PUT remain unverified.

## State

verifying; human acceptance pending
