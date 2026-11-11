# PR notes: Single-Owner Sign-In for Internet Access (feature 187)

## Performance note (constitution, Principle IV)

The access gate is on the path of each request. A test measures it
(`tests/unit/Performance/AccessGatePerformanceTests.cs`): 2000 requests with a valid session cookie go through
`AccessGateMiddleware` (classifier, address resolver, limiter, session lookup).

| Measure | Result | Budget |
|---------|--------|--------|
| p95 of one gate call | 0.0101 ms | less than 1 ms |
| Longest call | 1.94 ms (one outlier of 2000) | not limited |
| File writes for 2200 usual requests | 0 | none |

A session check is one in-memory lookup. The file write for a session use happens at most one time in each hour
for each session.

## Test results

| Suite | Result |
|-------|--------|
| Unit | 4926 passed |
| Integration | 1120 passed, 2 skipped (skips are older tests) |
| Contract | 1268 passed |
| Web UI (Jest) | 1621 passed in 216 suites. Lint: 0 errors, 1 old warning. Build: ok |
| Playwright | `auth-passkey.spec.ts` passed with the virtual authenticator (`--trace off`) |

## Coverage of the new code (unit, integration, and contract tests together)

`src/GameBot.Service/Security/` and `src/GameBot.Service/Endpoints/AuthEndpoints.cs`: 94.1% of lines (1177 of 1251) and
at least 84.8% of branches (473 of 558). The branch value is a lower bound, because the merge of the three reports takes the
largest covered count for each line. Target: 80% lines and 70% branches.

## CI shards (docs/ci-time.md)

`scripts/ci/measure-shards.ps1 -Project integration` showed `integration-rest` at 124.9 s. The average was 90.1 s
and the limit was 108.1 s, so the shard had to split. The new `GameBot.IntegrationTests.Security.` tests (about 30 s)
now run in `integration-pinned`. After the change: 91.0, 79.1, 76.7, 76.6, and 94.6 s. Average 83.6 s, limit 100.3 s.
All shards are within the limit.

The unit and contract shards were not measured again. The new tests add about 1 s (unit) and 2 s (contract),
and they run in the complement shards.

## Decisions to know

- **Swagger with the sign-in function off.** The Swagger pages stay open for the service machine and the home
  network, as before this feature. Many current contract tests read `/swagger/v1/swagger.json` with a token set and
  no bearer header. The internet gets the refusal A-2 for them. With the sign-in function on, Swagger needs a session or a token.
- **Where the access gate of the web UI lives.** `AccessGate` wraps `App` in `main.tsx` and not inside `App.tsx`.
  The five current `App` test files stay unchanged.
- **Playwright.** The test uses a mock service. The real service checks the `https` public origin, and a local test
  server uses `http://localhost`. The server checks of the passkey flow are in `RealVerifierFlowTests` (software authenticator).

## Open: manual check with a real proxy (task T067)

Not done in this session, because no real Traefik or nginx proxy was available. The owner must do the rows of
`quickstart.md` on a real phone through a real proxy on a different host, with the mobile network check, and measure
SC-001, SC-002, and SC-012. Until then, `docs/internet-access.md` marks both proxy examples as
"Not tested with a real proxy". Change the mark of an example to "Tested with a real proxy" only after that example passes.
The integration tests simulate both proxy header profiles (Traefik default and nginx default) with the remote address and the headers.
