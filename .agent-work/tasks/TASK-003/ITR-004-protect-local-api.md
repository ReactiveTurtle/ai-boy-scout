# ITR-004 - Protect the localhost API boundary

State: verifying (human acceptance pending).

## Decision

The user confirmed that the site is localhost-only. Firebase configuration will not be accepted before requests to `/api` from a non-loopback connection and browser writes from unrelated origins are refused. This is not multi-user authentication or a defense against other programs on the same computer.

## Scope

Add a request boundary before CORS and endpoint dispatch; leave local development port 4200 available. No Firebase credentials are stored or exposed in this iteration.

## Remaining risk

Reverse proxies that forward traffic from the network via loopback defeat IP-based access control. The site must not be reverse-proxied publicly. A credential form needs additional secret-at-rest protection and explicit authorization review before activation.
