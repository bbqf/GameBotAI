# Research: Single-Owner Sign-In for Internet Access

**Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

## R1. Passkey library on the service

- **Decision**: Use the NuGet package `Fido2` (fido2-net-lib), current stable 4.x. Pin the exact version in `GameBot.Service.csproj`. Put it behind the interface `IPasskeyVerifier`.
- **Rationale**: The service is on .NET 9. The built-in passkey support of ASP.NET Core starts with .NET 10 and is part of ASP.NET Core Identity, which has user accounts and passwords. The library does the WebAuthn checks (challenge, origin, RP ID, signature, sign count). The interface lets most tests use a fake.
- **Alternatives considered**: ASP.NET Core Identity passkeys (needs .NET 10 and a user store). Own WebAuthn code (security risk, large).

## R2. Passkey calls in the web UI

- **Decision**: Use `navigator.credentials.create` and `navigator.credentials.get` directly. Add a small base64url helper in `src/web-ui/src/lib/passkey.ts`. Add no npm package.
- **Rationale**: The JSON helper functions of the browser (`parseCreationOptionsFromJSON`, `toJSON`) are not in all current phone browsers. The conversion is about 40 lines. The repo rule is dependency hygiene.
- **Alternatives considered**: `@simplewebauthn/browser` (one more dependency for a small conversion).

## R3. Passkey options

- **Decision**: Discoverable credential (`residentKey: required`), `userVerification: required`, attestation `none`. One fixed user handle (16 random bytes, made one time). Sign-in sends no credential list (`allowCredentials` is empty). Registration sends `excludeCredentials` with the registered ids.
- **Rationale**: There is one owner, so there is no user name field. An empty `allowCredentials` shows nothing about the registered passkeys (FR-018). `userVerification: required` gives the screen lock check (FR-003).
- **Note**: A synced passkey (Google Password Manager, iCloud Keychain) works on all devices of that account after one registration. The spec permits this. The documentation tells the owner.

## R4. Session

- **Decision**: An opaque session token of 32 random bytes. The cookie is `__Host-gamebot-session`, with `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, and a 30-day `Max-Age`. The service keeps only the SHA-256 hash of the token, in `data/auth/sessions.json`. The service keeps the sessions in memory and reads the file again when the file time changes.
- **Sliding expiry**: Each use sets `lastUsedUtc` in memory. The service writes the file and sends a new cookie `Max-Age` at most one time in each hour for each session.
- **Rationale**: A session in a file stays valid after a restart (FR-013). The owner can end one session, the sessions of one passkey, or all sessions (FR-014, FR-011). `__Host-` and `Secure` keep the cookie off plain HTTP (FR-019). `HttpOnly` keeps it from page scripts (FR-019). `SameSite=Strict` stops requests from other sites (FR-020).
- **Alternatives considered**: ASP.NET cookie authentication with Data Protection. It needs a saved key ring and a second store for revocation, so it is not simpler. JWT: no revocation.

## R5. Protection from other web sites (FR-020)

- **Decision**: `SameSite=Strict`, plus an `Origin` check. For a request with a session cookie and a method other than GET, HEAD, or OPTIONS, an `Origin` header that is not the public origin gets the usual refusal.
- **Rationale**: Two independent layers. The web UI is a single-page application, so its API calls are same-site and the Strict cookie goes with them. A link from a notification opens the application shell first, which needs no cookie.

## R6. Where a request comes from (FR-015, FR-023)

This is the hardest part. The service listens on plain HTTP. The primary layout is a reverse proxy (Traefik or nginx) on a different host of the home network (User Story 4). It connects from a private address, the same as a phone on the home network. A gateway on the same machine connects from `127.0.0.1`.

- **Decision**: One class, `RequestOriginClassifier`, gives one of three results. The rules apply in this order, and the first match gives the result.
  1. The remote address is in `TrustedProxies` (research R12) → `Internet`. Headers do not change this result (FR-028). A loopback address is permitted in `TrustedProxies`. Then each request from the service machine is an `Internet` request.
  2. The request has a forward header → `Internet`.
  3. The remote address is loopback (or null, see below), AND the `Host` header is a loopback name (`localhost`, `127.0.0.1`, `[::1]`) → `LocalMachine`.
  4. The sign-in function is on, AND `TrustedProxies` is empty, AND the remote address is not loopback → `Internet`. In this condition the service cannot tell a proxy from a home network device, and the client controls the `Host` header. Thus the service enforces the mandatory setting: only loopback stays inside. The startup log has a critical entry.
  5. The remote address is private (RFC 1918, link-local, IPv6 unique local) or loopback, AND the `Host` header is not the host of the public origin → `HomeNetwork`.
  6. All other requests → `Internet`. A doubt gives `Internet`.
- **Why a setting for the proxy**: Traefik sends `X-Forwarded-*` headers and keeps the public `Host` by default, so rule 2 finds it. The default `proxy_pass` of nginx sends no forward header and changes `Host` to the address of the service. Such a request from a different host is the same as a request of a home network device. Only the address of the proxy tells them apart. So `TrustedProxies` is a mandatory setting for a proxy on a different host.
- **Forward headers**: `Forwarded`, `X-Forwarded-For`, `X-Forwarded-Host`, `X-Forwarded-Proto`, `X-Real-IP`, `CF-Connecting-IP`, `True-Client-IP`, `Via`. The service does not trust their values for access. It uses only their presence, and presence always lowers the trust.
- **Use**: With the sign-in function off, `LocalMachine` and `HomeNetwork` pass and `Internet` gets a refusal (FR-023). With the sign-in function on, the access token is valid for `LocalMachine` and `HomeNetwork`, and never for `Internet` (FR-015, second clarify session). A request with no token needs a session from all sources.
- **Null remote address**: Kestrel always gives an address for a TCP connection. The in-process test server gives null. Null counts as loopback, so the current tests continue to work. Tests set a remote address to simulate other sources.
- **Residual risk**: Two setups hide the source of a request. (a) A proxy on a different host that sends no forward header, when the owner did not set `TrustedProxies`. (b) A gateway on the same machine that sends no forward header and rewrites `Host` to a loopback name. No rule can find these cases with no setting. The setting closes the two cases: for (a), the owner sets the proxy address in `TrustedProxies`; for (b), the owner sets `127.0.0.1` and `::1` in `TrustedProxies`. With loopback in `TrustedProxies`, the access token works only on the home network, and the local command is not affected, because it uses no web request. The mitigations for an owner who forgets the setting are: (1) the setup guide makes `TrustedProxies` mandatory for a proxy on a different host and gives tested Traefik and nginx examples with forward headers; (2) the setup guide has a mandatory check from a phone on a mobile network with the sign-in function off; (3) with the sign-in function on, a wrong `HomeNetwork` or `LocalMachine` result gives access only to a person who also has the access token, and wrong tokens count for the limiter; (4) the guide recommends a firewall rule that permits the service port only from the proxy host.
- **Direct home network request with the sign-in function on**: This applies when `TrustedProxies` is set. With no trusted proxy, rule 4 gives `Internet`. The result is `HomeNetwork`. With the correct access token, the request passes. With no token or a wrong token, the request gets refusal A-1. The status call gives the public origin and tells if the token prompt has a use, and the page shows them (FR-031).
- **Alternatives considered**: A second listen port for the gateway (a wrong gateway target has the same failure, and it adds installer work). Trust in `X-Forwarded-For` values (a client can forge them).

## R7. Registration code and local command (FR-006, FR-007, FR-010, FR-012)

- **Decision**: The service executable gets a command mode: `GameBot.Service.exe auth <command>`. `Program.cs` examines the arguments before it builds the web host. The command reads and writes the files in `data/auth/` directly. The service has no endpoint that makes a code.
- **Commands**: `new-code`, `list`, `remove <id>`, `end-sessions`. See [contracts/auth-cli.md](contracts/auth-cli.md).
- **Code**: 8 characters from a 32-character alphabet with no look-alike characters, shown as `XXXX-XXXX` (40 bits). The file `registration-code.json` keeps the SHA-256 hash, the expiry (10 minutes), and a failed-attempt count. A new code replaces the file. A correct use deletes the file. 5 failed attempts delete the file.
- **Rationale**: Access to the data directory proves that the caller is on the service machine. No web path exists, so no request can get a code (also not a forwarded request). The same executable finds the same data directory and needs no new project and no installer change.
- **Shared files**: The service and the command use one store class. Each change is a read-modify-write under an exclusive file lock with retry. The service reads a file again when its last-write time changes (it examines the time at most one time in each second). So a removal by the command is in effect in about 1 second (SC-007).
- **Alternatives considered**: A named pipe to the service (more code, no gain). A separate tool project (installer change). A loopback-only endpoint (a gateway can reach it; see R6).

## R8. Limit on failed attempts (FR-016)

- **Decision**: An in-memory limiter for each source address. 5 failed attempts in 5 minutes block that source for 15 minutes. The limit applies to sign-in completion, to registration code checks, and, with the sign-in function on, to requests with a wrong access token (FR-016). A request with no token and no session is not a failed attempt, because each page load of a new device makes such requests. A blocked source gets the usual refusal. The source address is the remote address. Only when the remote address is in `TrustedProxies` or is loopback, the source address is the last `X-Forwarded-For` entry that is not in `TrustedProxies` (FR-029). If such a request has no usable entry, the source address is the remote address, and the service writes the record `proxy_no_client_address` one time in each day. Then all clients of the proxy share one limit.
- **Rationale**: A passkey signature has no guess attack, so the limiter protects the registration code and the CPU. The 5-attempt limit on the code itself (R7) is the hard limit for the code. No global lock, because that lets a stranger lock out the owner.
- **Pending ceremonies**: The service keeps at most 100 pending challenges, each for 5 minutes, and at most 5 for each source address. A 6th challenge of one source removes the oldest challenge of that source. When the total is 100, a new challenge removes the oldest challenge of the source that has the most. So one source cannot remove the pending sign-in of the owner (FR-016), and the memory has a limit.

## R9. What stays open with the sign-in function on

- **Decision**: These paths need no session: `/health`, the static files of the web UI and the fallback to `index.html`, `GET /api/auth/status`, the two sign-in calls, the two registration calls, and `OPTIONS` preflight. All other paths need a session or a local token. This includes `/swagger`.
- **Rationale**: The sign-in page is part of the single-page application, so the browser must get the application files first. These files are the same for all installations and hold no application data (FR-001, User Story 1 scenario 1). `/swagger` is open today; FR-001 closes it.

## R10. Refusals (FR-018, FR-023)

- **Decision**: Each refusal for sign-in, registration, session, token, or limiter is `401` with one fixed body: code `unauthorized`, message "Sign-in is necessary." A refusal by FR-023 is `403` with code `remote_access_refused` and a fixed message. The cause goes only to the sign-in record and the log.
- **Rationale**: One body for all causes shows nothing about passkeys or codes. The 403 for FR-023 is a different state (no sign-in is possible), and the web UI does not show a sign-in page for it.

## R11. Sign-in record (FR-017, FR-024)

- **Decision**: Append one JSON line for each event to `data/auth/sign-in-records.jsonl`, and write the same event to the log category `GameBot.Service.Security`. Roll the file at 1 MB and keep one old file. Events: `sign_in`, `sign_in_failed`, `sign_out`, `sessions_ended`, `passkey_registered`, `registration_failed`, `passkey_removed`, `source_blocked`, `remote_access_refused`. Write `remote_access_refused` at most one time in 10 minutes for each source.
- **Rationale**: The service log goes to the console, which a background service does not keep. A file gives the owner a record. The size limit stops a flood. The record has the time, the source, the event, and the passkey id. It has no token, no code, and no cookie.

## R12. Settings

- **Decision**: Three settings. `Service:Auth:Enabled` (environment `GAMEBOT_AUTH_ENABLED`, default `false`). `Service:Auth:PublicOrigin` (environment `GAMEBOT_AUTH_PUBLIC_ORIGIN`), for example `https://bot.example.com`. The RP ID is the host of the public origin. `Service:Auth:TrustedProxies` (environment `GAMEBOT_AUTH_TRUSTED_PROXIES`), a comma list of IP addresses and CIDR ranges, for example `192.168.1.5` or `172.18.0.0/16` (FR-027). The default is empty. An entry that is not valid gives a log error, and the service ignores that entry.
- **No change through the web**: The sign-in settings come only from the service configuration and the environment. The configuration endpoints (`/api/config`, configuration files) and the restore endpoint MUST NOT change `Service:Auth:*` (FR-027). A task verifies this with a test.
- **Public origin, not request headers**: All passkey checks, the cookie attributes, and the `Origin` rule use `PublicOrigin`. They do not use the `Host` header or `X-Forwarded-Proto`. So the flows work when the proxy keeps the host name and when the proxy changes it (FR-030). The service always sets `Secure` on the cookie, although its own listener is plain HTTP.
- **Listen address**: For a proxy on a different host, the service must listen on a home network address (`GAMEBOT_BIND_HOST`, feature 162). This feature does not change that setting.
- **Not valid**: If the sign-in function is on and the public origin is absent or is not `https`, the service starts, writes an error to the log, and refuses all sign-in and registration calls. Local token access continues to work.
- **Rationale**: Fail closed (FR-008), but do not stop the queues because of a setting error. The configuration snapshot shows the two rows with their sources, as it does for the network settings.

## R13. Web UI flow

- **Decision**: The application asks `GET /api/auth/status` at start. If the sign-in function is on and there is no access, the application shows the sign-in page in place of the content. The address stays the same, so the requested page shows after the sign-in (FR-021) with no redirect parameter. A `401` on a later call shows the sign-in page again. The path `/register` shows the registration page. The Configuration area gets a "Sign-in devices" section.
- **Token prompt**: With the sign-in function off, or on the service machine, the current token prompt stays as it is.
- **Rationale**: No redirect parameter means no open-redirect risk. The Configuration area already has phone level `full`.

## R14. Live connections

- **Finding**: The service has no WebSocket and no server-sent events. The web UI polls. A poll after the session ends gets `401`. The edge case of an open live connection needs no code.

## R15. Tests

- **Decision**:
  - Unit: classifier (table of address and header cases), limiter, registration code, session store, sign-in record, command parser.
  - Integration (`WebApplicationFactory`): gate rules for each mode and source, with a fake `IPasskeyVerifier`. One test class uses the real verifier with a software authenticator (ES256, attestation `none`) written in the test project.
  - Contract: the auth endpoints and the fixed refusal bodies.
  - Web UI: Jest tests for the pages and the helper. One Playwright test with the virtual authenticator of Chromium.
- **Rationale**: The fake keeps the gate tests fast. The software authenticator proves the real library path one time.

## R16. Internet path (out of scope for code)

- **Decision**: The feature adds no TLS and no tunnel. The new guide `docs/internet-access.md` has the primary layout first: a proxy on a different host, with a tested Traefik example and a tested nginx example (FR-032). The nginx example sets `Host`, `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host`. The guide also has one example of a gateway on the service machine, the `TrustedProxies` setting, the listen address, the firewall rule, and the mandatory check. `docs/phone-access.md` warning 3 gets a link to the new guide.
- **Hop between the proxy and the service**: Plain HTTP in the home network. The session cookie and the access token are readable on this hop by a person who can read home network traffic. The owner accepted this (second clarify session). HTTPS on the service listener is out of scope.
- **Tests with a proxy**: Integration tests simulate the proxy with the remote address and the headers of the request (both header profiles: Traefik default and nginx default). A test with a real Traefik or nginx is a manual check in quickstart.md. The guide marks each example as "Tested with a real proxy" or "Not tested with a real proxy", from the result of that check (FR-032).
