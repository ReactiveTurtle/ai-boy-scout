# ITR-011 - Retry an unverified note

Status: verifying

## Expected result

`Проверить снова` retries an unverified proposal while preserving explicit project consent.

## In scope

- allow retry from `unverified`;
- start paused automation when a permitted retry is requested;
- direct the user to project consent when access is absent.

## Primary risk

A retry could bypass project read consent.

## Decision

Retry never grants access. Without consent it selects the source project and explains the required action; with consent it starts processing and retries the proposal.

## Verification evidence

- Angular production build passed;
- Core and MCP test suite passed: 27 tests plus MCP smoke test;
- browser check confirmed the no-access path selects the source project and shows the consent message;
- the no-access browser check left proposal attempts unchanged at 3 and state unchanged at `unverified`.
