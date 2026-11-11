---

description: "Task list for feature 187: Single-Owner Sign-In for Internet Access"
---

# Tasks: Single-Owner Sign-In for Internet Access

**Input**: Design documents from `/specs/187-single-user-auth/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/auth-api.md, contracts/auth-cli.md, contracts/ui-contract.md, quickstart.md

**Tests**: Included. The constitution (Principle II) requires tests for all executable logic. Write each test before its implementation and make sure that it fails first.

**Organization**: Tasks are grouped by user story. The four P1 stories are in dependency order: US2 (the gate), US4 (the proxy on a different host), US3 (registration), then US1 (sign-in). A sign-in needs a registered passkey, and all need the gate.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1 to US6, as in spec.md)

## Path Conventions

- Service: `src/GameBot.Service/`
- Web UI: `src/web-ui/src/`, Playwright tests in `src/web-ui/tests/`
- Tests: `tests/unit/Security/`, `tests/integration/Security/`, `tests/contract/Security/`
- All new text (comments, messages, docs) obeys STE (constitution, Principle VI). Method names use CamelCase with no underscore.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The dependency, the routes, and the settings

- [X] T001 Add the NuGet package `Fido2` (current stable 4.x, exact version) to `src/GameBot.Service/GameBot.Service.csproj`. Run `dotnet build "C:\src\GameBot\GameBot.sln"` and make sure that it has no new warning.
- [X] T002 [P] Add the route constants `Auth`, `AuthStatus`, `AuthSignInOptions`, `AuthSignInComplete`, `AuthRegisterOptions`, `AuthRegisterComplete`, `AuthSignOut`, `AuthSessionsEndAll`, and `AuthPasskeys` to `src/GameBot.Service/ApiRoutes.cs` (paths of contracts/auth-api.md).
- [X] T003 [P] Create `src/GameBot.Service/Security/AuthOptions.cs`: `Enabled`, `PublicOrigin`, `TrustedProxies` (addresses and CIDR ranges, with `IsTrustedProxy(IPAddress)`), `Token`, the computed `RpId`, and `IsValid` (research R12). Read `Service:Auth:Enabled` / `GAMEBOT_AUTH_ENABLED`, `Service:Auth:PublicOrigin` / `GAMEBOT_AUTH_PUBLIC_ORIGIN`, `Service:Auth:TrustedProxies` / `GAMEBOT_AUTH_TRUSTED_PROXIES`, and `Service:Auth:Token` / `GAMEBOT_AUTH_TOKEN`. Add unit tests in `tests/unit/Security/AuthOptionsTests.cs`: default off, `https` necessary, RP ID from the host, one address, a CIDR range, IPv4-mapped IPv6, loopback addresses are permitted (FR-027), and an entry that is not valid is ignored with a problem text.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared building blocks that all stories use

**⚠️ CRITICAL**: No user story work can start before this phase is complete

- [X] T004 [P] Write unit tests in `tests/unit/Security/RequestOriginClassifierTests.cs`: a table of remote address, `Host`, and forward header cases for `LocalMachine`, `HomeNetwork`, and `Internet` (research R6). Include: null address, loopback with `X-Forwarded-For`, loopback with the public host, private address, public address, each forward header alone, IPv6 loopback, and IPv4-mapped IPv6. Include the trusted proxy cases (FR-028): a trusted proxy address with the Traefik default profile (forward headers, public `Host`) → `Internet`; with the nginx default profile (no forward header, `Host` is the service address) → `Internet`; an address in a trusted CIDR range → `Internet`; the same private address when it is not in `TrustedProxies` and has no forward header → `HomeNetwork`; loopback in `TrustedProxies` with a loopback `Host` and no forward header → `Internet` (FR-027).
- [X] T005 Create `src/GameBot.Service/Security/RequestOrigin.cs` (enum) and `src/GameBot.Service/Security/RequestOriginClassifier.cs` (research R6). Make T004 pass.
- [X] T006 [P] Write unit tests in `tests/unit/Security/AuthFileStoreTests.cs`: read of an absent file, read-modify-write, replace through a temporary file, reload after a change by a second instance, and lock retry.
- [X] T007 Create `src/GameBot.Service/Security/AuthFileStore.cs` (plan, Design 4): generic JSON store in `data/auth/`, exclusive file lock with retry, in-memory copy, reload when the last-write time changes (examined at most one time in each second, with `TimeProvider`). Make T006 pass.
- [X] T008 [P] Write unit tests in `tests/unit/Security/SignInRecorderTests.cs`: one JSON line for each event, no secret fields, roll at 1 MB to `sign-in-records.1.jsonl`, the 10-minute limit for `remote_access_refused` for each source, and the fixed `detail` text of `remote_access_refused` that tells the owner to set `GAMEBOT_AUTH_ENABLED=true` (data-model "Sign-in record", FR-024).
- [X] T009 Create `src/GameBot.Service/Security/SignInRecorder.cs` (research R11, data-model "Sign-in record"). Write the file and a `LoggerMessage` log entry in the category `GameBot.Service.Security`. Make T008 pass.
- [X] T010 [P] Write unit tests in `tests/unit/Security/FailedAttemptLimiterTests.cs`: 5 failures in 5 minutes block a source for 15 minutes, a different source is not blocked, the block ends, and old failures expire (use a fake `TimeProvider`). Write unit tests in `tests/unit/Security/ClientAddressResolverTests.cs` (FR-029): a trusted proxy with `X-Forwarded-For` gives the last entry that is not a trusted proxy; a chain of two trusted proxies; a loopback remote address with `X-Forwarded-For` (a gateway on the service machine) gives the last entry; a trusted proxy with no header gives the proxy address and the `proxy_no_client_address` record one time in each day; a request that is not from a trusted proxy or loopback ignores a forged `X-Forwarded-For`.
- [X] T011 Create `src/GameBot.Service/Security/FailedAttemptLimiter.cs` and `src/GameBot.Service/Security/ClientAddressResolver.cs` (research R8). Make T010 pass.
- [X] T012 [P] Create `src/GameBot.Service/Models/Auth.cs`: the request and response DTOs of contracts/auth-api.md C-1 to C-10. Create `src/GameBot.Service/Security/AuthRefusals.cs`: one shared writer for the two fixed refusal bodies A-1 and A-2.
- [X] T013 Register `AuthOptions`, `RequestOriginClassifier`, `ClientAddressResolver`, `SignInRecorder`, and `FailedAttemptLimiter` in a new method `RegisterAuthServices` of `src/GameBot.Service/GameBotServiceSetup.cs`. Do not change the token block of `src/GameBot.Service/Program.cs` in this task (T019 removes it).
- [X] T014 [P] Create the test helper `tests/integration/Security/AuthTestHost.cs`: a `WebApplicationFactory` wrapper that sets the auth settings, uses a temporary data directory, sets the remote address and headers of a request (a startup filter), has the two proxy header profiles (Traefik default, nginx default), and can replace `IPasskeyVerifier` with a fake.

**Checkpoint**: The foundation is ready. The current tests still pass, because no behavior changed yet.

---

## Phase 3: User Story 2 - The service refuses all other persons (Priority: P1) 🎯 MVP part 1

**Goal**: The access gate. All pages and endpoints refuse a request with no access. The sign-in function off refuses the internet.

**Independent Test**: With `AuthTestHost`, send requests from each source in each mode. Each request with no access gets A-1 or A-2 and no data. Local use with the sign-in function off works as before.

### Tests for User Story 2

- [X] T015 [P] [US2] Write integration tests in `tests/integration/Security/AccessGateOffModeTests.cs`: sign-in function off; `Internet` → 403 A-2 and a `remote_access_refused` record with the instruction text; `LocalMachine` and `HomeNetwork` → pass with no token configured; token rule as before when a token is configured; `/health` → 200 from all sources (FR-002, FR-023, FR-024, SC-008).
- [X] T016 [P] [US2] Write integration tests in `tests/integration/Security/AccessGateOnModeTests.cs`: sign-in function on; no session → 401 A-1 for a sample of GET and POST endpoints and for `/swagger`; correct token from `LocalMachine` and from `HomeNetwork` → pass; correct token from `Internet` → A-1; correct token from loopback with `X-Forwarded-For` → A-1; 5 wrong tokens from one source block that source, and a request with no token does not count (FR-016); `OPTIONS` and static files → pass; public origin not valid → sign-in endpoints give A-1 and token access works (FR-001, FR-015, SC-005, research R12).
- [X] T017 [P] [US2] Write contract tests in `tests/contract/Security/AuthRefusalContractTests.cs`: the A-1 body is the same bytes for no cookie, unknown cookie, wrong token, and a blocked source; the A-2 body matches the contract (FR-018). Write contract tests in `tests/contract/Security/AuthStatusContractTests.cs` for C-1: all fields of the contract, and no field shows if a passkey or a code exists.

### Implementation for User Story 2

- [X] T018 [US2] Create `src/GameBot.Service/Security/AccessGateMiddleware.cs` with the rule table of contracts/auth-api.md and the steps of plan Design 1. The session step calls an `ISessionValidator` interface (new file `src/GameBot.Service/Security/ISessionValidator.cs`) with a default implementation that returns "no session" (US1 completes it). Change `src/GameBot.Service/Security/TokenAuthMiddleware.cs` to a constant-time compare (`CryptographicOperations.FixedTimeEquals`) and to the shared refusal writer when the sign-in function is on.
- [X] T019 [US2] Change `src/GameBot.Service/Program.cs`: remove the current token block, add the access gate after the static files, and put `/swagger` behind the gate. Keep the pipeline order of plan Design 1.
- [X] T020 [US2] Add the endpoint C-1 `GET /api/auth/status` in a new file `src/GameBot.Service/Endpoints/AuthEndpoints.cs`, with all fields of the contract (`signInEnabled`, `signedIn`, `passkeysSupportedHere`, `source`, `publicOrigin`, `tokenAccessHere`). Map it in `src/GameBot.Service/Program.cs`. Make T015, T016, and T017 pass.
- [X] T021 [US2] Run the full unit, integration, and contract test projects. Correct each current test that the gate breaks (expected: none, because a null remote address counts as loopback). Update the API contract snapshot in `tests/contract/ApiContractSnapshots/` if the Swagger document changed (SC-011).
- [X] T022 [P] [US2] Add the three rows `GAMEBOT_AUTH_ENABLED`, `GAMEBOT_AUTH_PUBLIC_ORIGIN`, and `GAMEBOT_AUTH_TRUSTED_PROXIES` with their sources to the configuration snapshot in `src/GameBot.Service/Services/ConfigSnapshotService.cs`. Make sure that the token value stays hidden. Add a unit test next to the current snapshot tests in `tests/unit/Config/`.

**Checkpoint**: The service is closed to all requests with no access. No device can sign in yet.

---

## Phase 4: User Story 4 - The owner uses a proxy on a different host (Priority: P1) 🎯 MVP part 2

**Goal**: Each request from a configured proxy is an internet request. A home network device can use the direct address with the access token.

**Independent Test**: With `AuthTestHost`, send requests from a trusted proxy address with the two header profiles. Each request with no session gets a refusal in the two states of the sign-in function.

The classifier, the client address, and the settings of this story are in Phase 1 and Phase 2 (T003 to T005, T010, T011). The passkey flows through the proxy are in T030. The page for the direct address is in T043 and T049.

### Tests for User Story 4

- [X] T023 [P] [US4] Write integration tests in `tests/integration/Security/RemoteProxyGateTests.cs` for a proxy on a different host (remote address `192.168.1.5` in `TrustedProxies`), each with the Traefik default profile and the nginx default profile: sign-in function off → 403 A-2; sign-in function on with no session → 401 A-1; correct token with no session → A-1; `/health` → 200; C-1 gives `source` `internet`, `passkeysSupportedHere` true, `publicOrigin` null, and `tokenAccessHere` false. Add direct requests from `192.168.1.30` (not a proxy) with the sign-in function on: no token → A-1; correct token → pass; C-1 gives `source` `homeNetwork`, `passkeysSupportedHere` false, the public origin, and `tokenAccessHere` true when a token is configured and false when no token is configured. Add the same-machine gateway case: `127.0.0.1` and `::1` in `TrustedProxies`, a loopback request with a loopback `Host` and no forward header → A-2 with the sign-in function off, and a correct token → A-1 with the sign-in function on (FR-015, FR-023, FR-027, FR-028, FR-031, SC-010).

### Implementation for User Story 4

- [X] T024 [US4] Make T023 pass. Correct `src/GameBot.Service/Security/AccessGateMiddleware.cs`, `src/GameBot.Service/Security/RequestOriginClassifier.cs`, and `src/GameBot.Service/Endpoints/AuthEndpoints.cs` where a case fails. Add a startup log entry (category `GameBot.Service.Security`) in `src/GameBot.Service/GameBotServiceSetup.cs` that shows the sign-in state, the public origin, and the trusted proxy entries, and a warning when the sign-in function is on and `TrustedProxies` is empty.

**Checkpoint**: The gate is correct for a proxy on a different host, for a gateway on the service machine, and for a home network device.

---

## Phase 5: User Story 3 - The owner registers a device (Priority: P1) 🎯 MVP part 3

**Goal**: The local command makes a one-time code. A new device uses the code to register a passkey. The local command can remove a passkey.

**Independent Test**: Run `auth new-code`, register a passkey with the code, and make sure that the code does not work a second time.

### Tests for User Story 3

- [X] T025 [P] [US3] Write unit tests in `tests/unit/Security/RegistrationCodeStoreTests.cs`: code format `XXXX-XXXX` from the 32-character alphabet, only the hash is in the file, expiry after 10 minutes, a new code cancels the old code, a correct use deletes the file, the 5th failed attempt deletes the file, and the compare ignores case, spaces, and the hyphen (FR-010).
- [X] T026 [P] [US3] Write unit tests in `tests/unit/Security/PasskeyStoreTests.cs`: add, list, rename, remove, unique credential id, short id rule, maximum 20, one `userHandle` for all, and name rules (data-model "Passkey").
- [X] T027 [P] [US3] Write unit tests in `tests/unit/Security/AuthCommandTests.cs`: each command of contracts/auth-cli.md with its output and exit code, `--data-dir`, the warning when the sign-in function is off, the text when the public origin is absent, unknown commands, and `remove` ends the sessions of the passkey (FR-007, FR-012).
- [X] T028 [P] [US3] Create the test helper `tests/integration/Security/SoftwareAuthenticator.cs`: makes ES256 credentials, a `none` attestation response, and assertion responses with `System.Formats.Cbor` and `ECDsa`, for a given challenge, origin, and RP ID.
- [X] T029 [P] [US3] Write integration tests in `tests/integration/Security/RegistrationFlowTests.cs` (fake verifier): correct code → options → complete → 204, cookie, passkey saved, code deleted; wrong, expired, used, and absent code → A-1 and no passkey; 5 wrong codes cancel the code; 5 failures block the source; no endpoint returns a code for any source or session (FR-006, FR-007, SC-003).
- [X] T030 [P] [US3] Write integration tests in `tests/integration/Security/RealVerifierFlowTests.cs`: registration and sign-in with the real `Fido2PasskeyVerifier` and `SoftwareAuthenticator`; a wrong origin, a wrong challenge, and a changed signature each give A-1. Run the correct flow two times through a trusted proxy address: one time with the public `Host` and forward headers, and one time with `Host` set to the service address and no forward header (FR-030, SC-009).
- [X] T031 [P] [US3] Write unit tests in `tests/unit/Security/PendingCeremonyCacheTests.cs`: expiry after 5 minutes; at most 5 items for each source, and a 6th item removes the oldest item of that source; at most 100 items in total, and a new item then removes the oldest item of the source that has the most; 200 items from one source do not remove an item of a different source (FR-016, research R8).

### Implementation for User Story 3

- [X] T032 [P] [US3] Create `src/GameBot.Service/Security/RegistrationCodeStore.cs` on `AuthFileStore` (research R7). Make T025 pass.
- [X] T033 [P] [US3] Create `src/GameBot.Service/Security/PasskeyStore.cs` on `AuthFileStore`. Make T026 pass.
- [X] T034 [P] [US3] Create `src/GameBot.Service/Security/PendingCeremonyCache.cs` (research R8). Make T031 pass.
- [X] T035 [US3] Create `src/GameBot.Service/Security/IPasskeyVerifier.cs` and `src/GameBot.Service/Security/Fido2PasskeyVerifier.cs` with the options of research R3 (registration options, registration check, sign-in options, sign-in check). All checks use `AuthOptions.PublicOrigin`, not request headers (FR-030). Only this file uses the `Fido2` package.
- [X] T036 [US3] Create a minimal `src/GameBot.Service/Security/SessionStore.cs` on `AuthFileStore`: make a session (32 random bytes, save the SHA-256 hash, expiry 30 days after creation), remove the sessions of one passkey, remove all sessions, and write the cookie `__Host-gamebot-session` with the attributes of research R4. US1 adds validation. US5 adds the sliding expiry.
- [X] T037 [US3] Add the endpoints C-4 and C-5 to `src/GameBot.Service/Endpoints/AuthEndpoints.cs` (plan Design 2): limiter, code check, verifier, save the passkey, then delete the code, make a session, write the records. Register the new services in `RegisterAuthServices` of `src/GameBot.Service/GameBotServiceSetup.cs`. Make T029 and the registration part of T030 pass.
- [X] T038 [US3] Create `src/GameBot.Service/Security/AuthCommand.cs` with `new-code`, `list`, `remove`, `end-sessions`, and the usage text (contracts/auth-cli.md). Add the argument check at the start of `src/GameBot.Service/Program.cs`, before `WebApplication.CreateBuilder`. Use the same storage root function as the service (extract it from `GameBotServiceSetup.ConfigureServices` into one internal method). Make T027 pass.
- [X] T039 [P] [US3] Create `src/web-ui/src/lib/passkey.ts` (base64url helper, `createPasskey`, `getPasskey`, `isPasskeySupported`) and `src/web-ui/src/services/authApi.ts` (calls C-1 to C-10). Add Jest tests in `src/web-ui/src/lib/__tests__/passkey.spec.ts` and `src/web-ui/src/services/__tests__/authApi.spec.ts`.
- [X] T040 [US3] Create `src/web-ui/src/pages/RegisterDevicePage.tsx` (contracts/ui-contract.md U-3) with a phone layout at 375 px. Show it for the path `/register` in `src/web-ui/src/App.tsx`. Add Jest tests in `src/web-ui/src/pages/__tests__/RegisterDevicePage.spec.tsx` (fields, one failure text, success, no passkey support) (FR-025).

**Checkpoint**: The owner can register a device and remove it with the local command. The device has a session cookie, but the gate does not accept it before US1 (T046).

---

## Phase 6: User Story 1 - The owner signs in from a phone (Priority: P1) 🎯 MVP part 4

**Goal**: The owner signs in with a passkey, sees the requested page, and the session passes the gate.

**Independent Test**: With one registered passkey, open a page with no session, sign in, see that page, and load it again with no new sign-in.

### Tests for User Story 1

- [X] T041 [P] [US1] Write integration tests in `tests/integration/Security/SignInFlowTests.cs` (fake verifier): options have an empty `allowCredentials` and are the same with zero passkeys; complete with a registered passkey → 204 and cookie, `lastUsedUtc` and `signCount` updated, `sign_in` record; passkey not registered, unknown ceremony, expired ceremony, and zero passkeys → A-1 and `sign_in_failed` record; 5 failures block the source (FR-003, FR-008, FR-016, FR-017, SC-006).
- [X] T042 [P] [US1] Write unit tests in `tests/unit/Security/SessionStoreTests.cs` (validation part): validate by hash, an unknown token fails, an expired token fails, and a session of a removed passkey fails. Write integration tests in `tests/integration/Security/SessionGateTests.cs`: a session cookie from sign-in and from registration passes the gate for GET and POST; an expired session → A-1; a non-GET request with a session and a foreign `Origin` → A-1; the same request with the public origin or no `Origin` → pass; the cookie has `Secure`, `HttpOnly`, `SameSite=Strict`, `Path=/`, and the `__Host-` prefix; C-1 gives `signedIn` true with a session (FR-019, FR-020, SC-004).
- [X] T043 [P] [US1] Write Jest tests in `src/web-ui/src/pages/__tests__/SignInPage.spec.tsx` and `src/web-ui/src/lib/__tests__/authStatus.spec.ts`: the gate cases of contracts/ui-contract.md U-1 (the direct address case included: the public address link always, and the token prompt only when `tokenAccessHere` is true, FR-031) and the page rules of U-2 (no password field, no registration link, one failure text, no error after a cancel) (FR-004).

### Implementation for User Story 1

- [X] T044 [US1] Add the endpoints C-2 and C-3 to `src/GameBot.Service/Endpoints/AuthEndpoints.cs` (plan Design 2). Make T041 and the sign-in part of T030 pass.
- [X] T045 [US1] Add validation to `src/GameBot.Service/Security/SessionStore.cs` and implement `ISessionValidator` with it. Make the unit tests of T042 pass.
- [X] T046 [US1] Complete the session step and the `Origin` rule in `src/GameBot.Service/Security/AccessGateMiddleware.cs` (research R5). Set `signedIn` in C-1 from the session. Make the integration tests of T042 pass.
- [X] T047 [P] [US1] Create `src/web-ui/src/lib/authStatus.ts`: the status signal of C-1, refresh, and the reaction to 401 and to 403 `remote_access_refused`. Change `src/web-ui/src/lib/api.ts` and `src/web-ui/src/lib/unauthorized.ts` so that a 403 with that code reaches `authStatus`.
- [X] T048 [P] [US1] Create `src/web-ui/src/pages/SignInPage.tsx` (contracts/ui-contract.md U-2) with a phone layout at 375 px (FR-025).
- [X] T049 [US1] Add `src/web-ui/src/components/auth/AccessGate.tsx` (shown by `src/web-ui/src/main.tsx` around `App`, so the current `App` tests stay unchanged) as the access gate of U-1: sign-in page, token prompt, public address text with link, refusal text, or content. Keep the address at sign-in and load the data again afterward (FR-021). With the sign-in function off, the UI must be the same as before (U-5). Make T043 pass.

**Checkpoint**: The owner registers, signs in, and uses the web UI. All other persons get a refusal. A session ends 30 days after the sign-in.

---

## Phase 7: User Story 5 - The owner stays signed in on a trusted device (Priority: P2)

**Goal**: Each use extends the session to 30 days. The session stays valid after a restart.

**Independent Test**: Sign in, restart the service, and open a protected page with no new sign-in.

### Tests for User Story 5

- [X] T050 [P] [US5] Add unit tests to `tests/unit/Security/SessionStoreTests.cs` (sliding part): each use moves the expiry to 30 days after that use, the file write occurs at most one time in each hour for each session, a new store instance reads the sessions from the file, and expired sessions go at a write (FR-013).
- [X] T051 [P] [US5] Write integration tests in `tests/integration/Security/SessionLifeTests.cs`: a session survives a host restart on the same data directory; a session with a use on day 29 is valid on day 40; a session with no use for 30 days → A-1; the response refreshes the cookie `Max-Age` at most one time in each hour (FR-013, SC-004).

### Implementation for User Story 5

- [X] T052 [US5] Add the sliding expiry with the one-hour write limit and the cookie refresh to `src/GameBot.Service/Security/SessionStore.cs` and to the session step of `src/GameBot.Service/Security/AccessGateMiddleware.cs`. Make T050 and T051 pass.
- [X] T053 [US5] Write the Playwright test `src/web-ui/tests/e2e/auth-passkey.spec.ts` with the virtual authenticator of Chromium (CDP `WebAuthn.addVirtualAuthenticator`): register with a code, reload and stay signed in, end the session, sign in again, and see the same page. The service is a mock, because the real service checks the https public origin. Run it with `--trace off`.

**Checkpoint**: The MVP is complete.

---

## Phase 8: User Story 6 - The owner signs out and manages devices (Priority: P3)

**Goal**: Sign-out, end of all sessions, and the list of registered devices with rename and remove in the web UI.

**Independent Test**: Sign in on two clients, end all sessions on one, and see the sign-in page on the two. Remove one passkey in the list and make sure that it cannot sign in.

### Tests for User Story 6

- [X] T054 [P] [US6] Write integration tests in `tests/integration/Security/SessionEndTests.cs`: C-6 ends only this session and clears the cookie; C-7 ends all sessions; C-8 lists passkeys with `current` and no key material; C-9 renames and rejects a name that is not valid; C-10 removes a passkey and ends its sessions; `auth remove` and `auth end-sessions` take effect in the running service in about 1 second; each action writes its record (FR-011, FR-012, FR-014, SC-007).
- [X] T055 [P] [US6] Write Jest tests in `src/web-ui/src/components/auth/__tests__/SignInDevicesSection.spec.tsx`: the list, the "This device" mark, rename, remove with confirmation, the warning for this device and for the last passkey, "Sign out", and "Sign out all devices" (contracts/ui-contract.md U-4).

### Implementation for User Story 6

- [X] T056 [US6] Add the endpoints C-6 to C-10 to `src/GameBot.Service/Endpoints/AuthEndpoints.cs`. Make T054 pass.
- [X] T057 [US6] Create `src/web-ui/src/components/auth/SignInDevicesSection.tsx` (U-4, one column on a phone, touch targets of 44 px or more) and show it in `src/web-ui/src/pages/Configuration.tsx` when `signInEnabled` is true. Make T055 pass (FR-025).

**Checkpoint**: All user stories work.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T058 [P] Verify that the backup, restore, and configuration file endpoints do not read or write `data/auth/` (`src/GameBot.Service/Services/BackupService.cs`, `src/GameBot.Service/Endpoints/ConfigFilesEndpoints.cs`). Add an integration test in `tests/integration/Security/AuthDataIsolationTests.cs`. Correct the code if the test fails (plan Design 6).
- [X] T059 [P] Verify that no endpoint can change `Service:Auth:*` (sign-in state, public origin, trusted proxies, token): the configuration endpoints in `src/GameBot.Service/Endpoints/` (`/api/config`, configuration files) and the restore endpoint. Add an integration test in `tests/integration/Security/AuthSettingsIsolationTests.cs` that tries each write path with a valid session and reads the settings again. Correct the code if the test fails (FR-027, plan Design 6).
- [X] T060 [P] Add a Swagger schema filter `src/GameBot.Service/Swagger/AuthSchemaFilter.cs` with STE descriptions of the auth DTOs, and register it in `src/GameBot.Service/GameBotServiceSetup.cs`. Update the API contract snapshot in `tests/contract/ApiContractSnapshots/`.
- [X] T061 [P] Write `docs/internet-access.md` in STE: prerequisites, the primary layout first (a proxy on a different host, with one Traefik example and one nginx example that sets `Host`, `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host`), the mandatory `GAMEBOT_AUTH_TRUSTED_PROXIES` setting, the listen address, the firewall rule for the service port, the plain HTTP hop in the home network, direct home network use with the access token, the local DNS step for a router with no NAT loopback, one example of a gateway on the service machine with `127.0.0.1,::1` as trusted proxies, the settings, the mandatory check from a mobile network (quickstart.md), device registration, the lost-device steps, the domain name change, synced passkeys, the sign-in record file, and the residual risk of research R6. Mark each proxy example as "Not tested with a real proxy" until T067 changes the mark (FR-026, FR-032).
- [X] T062 [P] Update `docs/phone-access.md` (warning 3 points to the new guide; remove the "Follow-up idea" that FR-023 now covers), `SECURITY.md`, and `ENVIRONMENT.md` (the three new variables and the changed token rule) (FR-022).
- [X] T063 Update `docs/architecture.md`: the security model, the auth endpoints, the `data/auth/` files, the command mode, and the "Last reviewed" date (constitution, Principle V).
- [X] T064 [P] Measure the access gate: add a test in `tests/unit/Performance/AccessGatePerformanceTests.cs` that shows a session check below 1 ms (p95) and no file write on a usual request. Put the result in `specs/187-single-user-auth/pr-notes.md` as the performance note.
- [X] T065 Run the coverage of the touched areas and make sure of 80% line and 70% branch or more for `src/GameBot.Service/Security/` and `src/GameBot.Service/Endpoints/AuthEndpoints.cs`. Add tests where the value is lower.
- [X] T066 Measure the CI shards with `scripts/ci/measure-shards.ps1`. Split a shard again if it is more than 20% above the average of its project (`docs/ci-time.md`).
- [ ] T067 Do the checks of `specs/187-single-user-auth/quickstart.md` on a real phone through a real proxy on a different host (Traefik or nginx), the US4 rows, the three time targets (SC-001, SC-002, SC-012), and the mandatory mobile network check included. Record the results in `specs/187-single-user-auth/pr-notes.md`. Change the mark of each example in `docs/internet-access.md` to "Tested with a real proxy" only for a proxy that passed. If no proxy is available, record that this check is open for the owner and leave the marks.
- [X] T068 Run `dotnet build`, all three .NET test projects, and the web UI `lint`, `test`, and `build`. All must pass with no new warning.
- [X] T069 Set `**Status**: Implemented` in `specs/187-single-user-auth/spec.md`, add the row to `specs/STATUS.md`, add the CHANGELOG entry in `CHANGELOG.md`, and bump `installer/versioning/version.override.json` as CLAUDE.md says.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: needs Setup. Blocks all stories.
- **US2 (Phase 3)**: needs Foundational.
- **US4 (Phase 4)**: needs US2 (the gate and the status endpoint).
- **US3 (Phase 5)**: needs US2 (the gate and the endpoint file). It does not need US4, but T030 uses the proxy profiles of T014.
- **US1 (Phase 6)**: needs US3 (passkey store, verifier, minimal session store, `authApi.ts`).
- **US5 (Phase 7)**: needs US1.
- **US6 (Phase 8)**: needs US1.
- **Polish (Phase 9)**: needs all stories. T058, T059, T061, and T062 can start after US2.

### Why the P1 stories are one MVP

A sign-in (US1) has no meaning with no registered passkey (US3). The two have no meaning with no gate (US2). The owner cannot use them from the internet with no proxy rules (US4). Each phase still ends with its own automated tests that pass.

### Within Each User Story

- Write the tests first and see them fail.
- Stores before endpoints. Endpoints before web UI pages.

### Parallel Opportunities

- Phase 1: T002 and T003.
- Phase 2: T004, T006, T008, T010, T012, and T014 together. Each implementation task follows its own test task.
- US2: T015, T016, and T017 together; T022 with T018.
- US3: T025 to T031 together; then T032, T033, T034, and T039 together.
- US1: T041, T042, and T043 together; T047 and T048 together.
- US5: T050 and T051 together.
- US6: T054 and T055 together.
- Polish: T058 to T062 and T064 together.

---

## Parallel Example: User Story 3

```text
# Tests first, all together:
Task: "RegistrationCodeStoreTests in tests/unit/Security/RegistrationCodeStoreTests.cs"
Task: "PasskeyStoreTests in tests/unit/Security/PasskeyStoreTests.cs"
Task: "AuthCommandTests in tests/unit/Security/AuthCommandTests.cs"
Task: "SoftwareAuthenticator in tests/integration/Security/SoftwareAuthenticator.cs"
Task: "RegistrationFlowTests in tests/integration/Security/RegistrationFlowTests.cs"
Task: "PendingCeremonyCacheTests in tests/unit/Security/PendingCeremonyCacheTests.cs"

# Then the independent files:
Task: "RegistrationCodeStore in src/GameBot.Service/Security/RegistrationCodeStore.cs"
Task: "PasskeyStore in src/GameBot.Service/Security/PasskeyStore.cs"
Task: "PendingCeremonyCache in src/GameBot.Service/Security/PendingCeremonyCache.cs"
Task: "passkey.ts and authApi.ts in src/web-ui/src/"
```

---

## Implementation Strategy

### MVP (Phases 1 to 6)

1. Setup and Foundational.
2. US2: the service is closed. This phase alone is safe to merge, because the sign-in function is off by default and FR-023 only adds protection.
3. US4: the proxy rules.
4. US3 and US1: registration, sign-in, and the session gate.
5. **Stop and validate**: do the quickstart checks. The owner can now expose the service. A session ends 30 days after the sign-in.

### After the MVP

6. US5: the session extends with each use.
7. US6: sign-out and device management in the UI. Before US6, the owner uses `auth remove` and `auth end-sessions` on the service machine.
8. Polish: docs, performance note, coverage, version bump.

---

## Notes

- Commit after each phase, with a passing build and passing tests (constitution: a red build stops all work).
- Never write a token, a code, a cookie value, or key material to a log, a record, or a test output.
- If the `Fido2` API differs from research R1 and R3, change only `Fido2PasskeyVerifier.cs` and record the difference in `research.md`.
