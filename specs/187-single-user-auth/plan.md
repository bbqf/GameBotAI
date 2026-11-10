# Implementation Plan: Single-Owner Sign-In for Internet Access

**Branch**: `187-single-user-auth` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/187-single-user-auth/spec.md`

## Summary

The owner wants to open the web UI from the internet, with a sign-in that only the owner can pass. The service gets passkey sign-in (WebAuthn) with server-side sessions in a cookie. A new device needs a one-time registration code. Only a local command mode of the service executable makes this code. The sign-in function is off by default. While it is off, the service refuses requests from the internet. While it is on, the existing access token works only on the service machine and on the home network, and never from the internet.

The primary network layout is a reverse proxy (Traefik or nginx) on a different host of the home network. The service machine is not exposed to the internet (User Story 4). The owner configures the address of the proxy, and the service treats each request from that address as an internet request.

The technical approach: one access gate middleware replaces the current token block in `Program.cs`. A request classifier gives the source of each request (service machine, home network, or internet). It uses the `TrustedProxies` setting first, then the forward headers, then the addresses. The NuGet package `Fido2` does the passkey checks behind an interface. Files in `data/auth/` keep the passkeys, the sessions, the registration code, and the sign-in record. The web UI gets a sign-in page, a registration page, and a "Sign-in devices" section in Configuration.

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`), TypeScript 5.6 with React 18 for the web UI
**Primary Dependencies**: ASP.NET Core minimal API, new NuGet package `Fido2` 4.x (pinned), no new npm package
**Storage**: JSON files in `data/auth/` (`passkeys.json`, `sessions.json`, `registration-code.json`, `sign-in-records.jsonl`)
**Testing**: xUnit (`tests/unit`, `tests/integration`, `tests/contract`), Jest (web UI), one Playwright test with the virtual authenticator
**Target Platform**: Windows service host (GameBot.Service), phone and desktop browsers with passkey support
**Project Type**: web-service with web-ui
**Performance Goals**: The access gate adds less than 1 ms (p95) to a request. A session check is one in-memory lookup. The gate does no file write on a usual request (at most one write in each hour for each session).
**Constraints**: Fail closed. One fixed refusal body for all causes. No secret in a file (only hashes), in a log, or in a record. No endpoint makes a registration code. Current local installations and current tests behave the same with the sign-in function off.
**Scale/Scope**: One owner, at most 20 passkeys, a small number of sessions. About 19 new service files, 4 changed service files, 6 new web UI files, 3 changed web UI files, tests, 3 docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: Build and test runs must pass before implementation progresses. Plan: run `dotnet build`, the unit, integration, and contract tests of the touched areas, and the web UI lint and tests before each commit.

*NON-NEGOTIABLE*: All text in this plan and in its artifacts obeys STE (Principle VI). Status: obeyed.

*Phone check (Principle III)*: The feature changes the web UI. The sign-in page, the registration page, and the "Sign-in devices" section have phone support level `full`. They follow `docs/mobile-ui-principles.md` (one column at 375 px, touch targets of 44 px or more). The section is in the Configuration area, which has level `full`. No new navigation area.

- I. Code quality: small classes with one task each; methods under about 50 lines; CamelCase names with no underscore. One new dependency (`Fido2`), pinned, with a reason (research R1). No secret in the repo. Pass.
- II. Testing: unit tests for each new class; integration tests for the gate rules and the full flows; contract tests for the endpoints. Coverage target of the new code: 80% line, 70% branch or more. Pass.
- III. UX consistency: refusals use the current error body shape (`error.code`, `error.message`, `error.hint`). The command has usage text and exit codes. Phone level stated. Pass.
- IV. Performance: goal stated above. The gate is on the hot path of each request, so the PR gets a performance note with a measurement. Pass.
- V. Living documentation: update `docs/architecture.md` (security, API surface, persistence layout, "Last reviewed"). Add `docs/internet-access.md`. Update `docs/phone-access.md`, `SECURITY.md`, `ENVIRONMENT.md`, and `specs/STATUS.md`. Set the spec `Status`. Pass.
- VI. STE: obeyed in all new text. Pass.
- Version bump and CHANGELOG entry: before the PR.

**Result after Phase 1 design**: no violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/187-single-user-auth/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── auth-api.md
│   ├── auth-cli.md
│   └── ui-contract.md
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Program.cs                          # command mode check; access gate replaces the token block; map auth endpoints
├── GameBotServiceSetup.cs              # register the auth services and options
├── ApiRoutes.cs                        # Auth routes
├── GameBot.Service.csproj              # Fido2 package
├── Security/
│   ├── AuthOptions.cs                  # Enabled, PublicOrigin, TrustedProxies, Token; validation
│   ├── RequestOrigin.cs                # enum LocalMachine, HomeNetwork, Internet
│   ├── RequestOriginClassifier.cs      # research R6
│   ├── AccessGateMiddleware.cs         # the rule table of contracts/auth-api.md
│   ├── TokenAuthMiddleware.cs          # keep; constant-time compare; used by the gate
│   ├── AuthFileStore.cs                # locked read-modify-write; reload on file time change
│   ├── PasskeyStore.cs                 # passkeys.json
│   ├── SessionStore.cs                 # sessions.json; hash, sliding expiry
│   ├── RegistrationCodeStore.cs        # registration-code.json
│   ├── FailedAttemptLimiter.cs         # research R8
│   ├── ClientAddressResolver.cs        # source address of a request (FR-029)
│   ├── AuthRefusals.cs                 # the two fixed refusal bodies
│   ├── SignInRecorder.cs               # sign-in-records.jsonl and log
│   ├── IPasskeyVerifier.cs             # interface
│   ├── Fido2PasskeyVerifier.cs         # the only file that uses the Fido2 package
│   ├── PendingCeremonyCache.cs         # in-memory challenges
│   └── AuthCommand.cs                  # `auth new-code|list|remove|end-sessions`
├── Endpoints/AuthEndpoints.cs          # C-1 to C-10
└── Models/Auth.cs                      # request and response DTOs

src/web-ui/src/
├── lib/passkey.ts                      # base64url helper; create and get calls
├── lib/authStatus.ts                   # status signal; reacts to 401 and 403
├── services/authApi.ts                 # calls of contracts/auth-api.md
├── pages/SignInPage.tsx
├── pages/RegisterDevicePage.tsx
├── components/auth/SignInDevicesSection.tsx   # shown in pages/Configuration.tsx
├── App.tsx                             # access gate (U-1) and /register path
├── lib/api.ts                          # report 403 remote_access_refused
└── lib/unauthorized.ts                 # work with authStatus

tests/unit/Security/                    # classifier, limiter, stores, code, recorder, command
tests/integration/Security/             # gate rules, flows with fake verifier, one real-verifier class, software authenticator helper
tests/contract/                         # auth endpoints and refusal bodies
src/web-ui/src/**/__tests__/            # pages, helper, gate
src/web-ui/tests/                       # one Playwright test with the virtual authenticator

docs/internet-access.md                 # new owner guide
docs/phone-access.md, docs/architecture.md, SECURITY.md, ENVIRONMENT.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: Existing layout. No new project. All service code of the feature is in `src/GameBot.Service/Security/`, with one endpoint file and one model file. The domain project does not change, because sign-in is a concern of the host.

## Design

### 1. Request pipeline

The order in `Program.cs` becomes: Swagger registration, error handling, correlation ids, CORS, static files, **access gate**, endpoints. The access gate replaces the current `if (!string.IsNullOrWhiteSpace(authToken))` block.

The gate does these steps for each request:

1. Pass `OPTIONS` and `/health`.
2. Get the source from `RequestOriginClassifier` (a request from a trusted proxy is always `Internet`).
3. Sign-in function off: source `Internet` → refusal A-2 and a record (FR-023, FR-024). Other sources → the current token rule (token necessary only when a token is configured).
4. Sign-in function on:
   - Pass the open auth endpoints (C-1 to C-5) and the web UI fallback.
   - Source `LocalMachine` or `HomeNetwork` with a correct bearer token → pass. A wrong bearer token from any source counts for the limiter.
   - Valid session cookie → examine the `Origin` rule, extend the session, pass.
   - All other cases → refusal A-1.

`/swagger` is behind the gate in the two modes. Static files are before the gate, because the sign-in page needs them (research R9).

### 2. Sign-in and registration

- Sign-in: C-2 makes a challenge and keeps it in `PendingCeremonyCache`. C-3 gets the challenge by `ceremonyId`, finds the passkey by credential id, and calls `IPasskeyVerifier`. On success it updates `signCount` and `lastUsedUtc`, makes a session, and sets the cookie.
- Registration: C-4 examines the code (limiter, then `RegistrationCodeStore`). C-5 verifies the new credential, adds the passkey, deletes the code, and makes a session. The code is deleted only after the passkey is saved.
- Each failure goes to `FailedAttemptLimiter` and `SignInRecorder`, and gives refusal A-1.

### 3. Local command

`Program.cs` starts with: if the first argument is `auth`, run `AuthCommand` and return its exit code. This is before `WebApplication.CreateBuilder`. The command resolves the storage root with the same function as the service and uses the same store classes. See [contracts/auth-cli.md](contracts/auth-cli.md).

### 4. Files shared by two processes

`AuthFileStore` gives read, and read-modify-write with an exclusive file lock and a short retry. It writes to a temporary file and then replaces the target file. The service side keeps a copy in memory and loads the file again when the last-write time changes. It examines the time at most one time in each second.

### 5. Web UI

`authStatus.ts` holds the status of C-1. `App.tsx` shows `SignInPage`, `RegisterDevicePage`, the token prompt, or the content, as [contracts/ui-contract.md](contracts/ui-contract.md) U-1 says. The address does not change at sign-in, so the requested page shows afterward (FR-021). API calls already go to the same origin, so the browser sends the cookie with no code change in `api.ts`.

### 6. Backup, restore, and configuration endpoints

The backup, restore, and configuration file endpoints MUST NOT read or write `data/auth/`. The configuration endpoints and the restore endpoint MUST NOT change `Service:Auth:*` (FR-027). Two tasks verify this and add tests.

### 7. Risks

| Risk | Mitigation |
|------|------------|
| A proxy on a different host sends no forward header, and `TrustedProxies` is not set (research R6). | `TrustedProxies` is mandatory in the guide. Tested Traefik and nginx examples. Mandatory check from a mobile network. With the sign-in function on, a home network result gives access only with the access token, and wrong tokens count for the limiter. Firewall rule for the service port. |
| A gateway on the same machine hides the source of a request (research R6). | The owner sets `127.0.0.1,::1` in `TrustedProxies`. Example in the guide. Mandatory check. |
| One source fills the pending ceremony cache. | At most 5 pending ceremonies for each source (research R8). |
| The proxy sends no client address. | All proxy clients share one limit on failed attempts. The service writes a record. The guide gives the header lines. |
| The proxy address changes. | The setting accepts CIDR ranges. Requests from an unknown private address with forward headers are still internet requests (rule 2). |
| The owner loses all devices. | `auth new-code` on the service machine (FR-012). The guide has the steps. |
| The public domain name changes. | Passkeys do not work at the new name. The guide tells the owner to remove them and register again. |
| The `Fido2` API differs from the plan. | Only `Fido2PasskeyVerifier.cs` uses the package. A real-verifier test with a software authenticator proves the path. |
| Current integration tests fail because of the gate. | A null remote address counts as loopback (research R6). The full test suite runs before the first commit of the gate. |

## Requirement map

| Requirement | Design element |
|-------------|----------------|
| FR-001, FR-002 | Access gate steps 1 and 4; research R9 |
| FR-003, FR-004, FR-005, FR-008, FR-009 | Passkey options (R3), `PasskeyStore`, C-2 and C-3 |
| FR-006, FR-007, FR-010 | `RegistrationCodeStore`, `AuthCommand`, C-4 and C-5 |
| FR-013, FR-014 | `SessionStore` (R4), C-6 and C-7 |
| FR-015 | Classifier (R6), gate step 4 |
| FR-016 | `FailedAttemptLimiter` (R8) |
| FR-017, FR-024 | `SignInRecorder` (R11) |
| FR-018 | Fixed refusal A-1 (R10) |
| FR-019, FR-020 | Cookie attributes (R4), `Origin` rule (R5) |
| FR-021 | Web UI gate with no redirect (R13) |
| FR-022, FR-023 | `AuthOptions` (R12), gate step 3 |
| FR-025 | UI contract, phone level `full` |
| FR-026, FR-032 | `docs/internet-access.md` |
| FR-027, FR-028 | `AuthOptions.TrustedProxies` (R12), classifier rule 1 (R6) |
| FR-029 | Source address function (R8) |
| FR-030 | Checks use `PublicOrigin`, not request headers (R12) |
| FR-031 | C-1 fields `source`, `publicOrigin`, and `tokenAccessHere`, UI contract U-1 |
| FR-011, FR-012 | C-8 to C-10, `auth list` and `auth remove` |

## Phase 0 and Phase 1 outputs

See [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), and [quickstart.md](quickstart.md).
