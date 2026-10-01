# ITR-010 - Increase interface type size

Status: verifying

## Expected result

Primary content, controls, metadata, and technical details are easier to read without breaking desktop or mobile layouts.

## In scope

- increase list, detail, navigation, filter, warning, and action text sizes;
- preserve the existing density and layout structure.

## Primary risk

Larger labels could overflow narrow controls or the mobile project strip.

## Verification evidence

- Angular production build passed;
- 1536 x 1024 desktop screenshot reviewed;
- 500 x 900 mobile screenshot reviewed;
- filters, warning actions, list rows, and detail panel remain usable.
