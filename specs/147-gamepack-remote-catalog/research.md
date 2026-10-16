# Research: Remote gamepack catalog

## R1. Index format

- Decision: One JSON document `index.json`: `{ "formatVersion": 1, "gamepacks": [ { "name", "versions": [ { "version", "changelog", "file", "signature", "sha256" } ] } ] }`. `signature` and `sha256` are optional.
- Rationale: Plain JSON is easy to host on any static server. A detached `signature` location lets the existing feature 146 check run without a new rule.
- Alternatives: One file for each gamepack (more requests); a custom API protocol (needs a server, out of scope).

## R2. Where the signature comes from

- Decision: The index entry may list a `signature` file location. The platform downloads it and passes its text as `signatureText` to `InstallAsync`. With no entry, the signature is null, and the policy decides.
- Rationale: SC-006 needs the same policy as a manual install. A manual install sends the signature as a second part.
- Alternatives: A fixed name rule (file + ".sig"): less flexible.

## R3. Reuse of the install flow

- Decision: Call `GamepackService.InstallAsync(json, dryRun, signatureText, ct)`. Map `GamepackInstallOutcome` to the same response DTO shape as the manual install route.
- Rationale: No second install path. The write gate and the lock stay in one place.
- Alternatives: Call the HTTP route in process (extra layer).

## R4. HTTP client and redirects

- Decision: Use `IHttpClientFactory` with `AllowAutoRedirect = false`. Follow at most 3 redirects by hand. Allow a redirect only to the same host and the same scheme. Set the bearer header on each hop to the same host only. Time out each request after 30 seconds.
- Rationale: Auto redirect can send the header to another host. Manual loop gives full control (FR-011).
- Alternatives: Default redirect with header strip (the rule is hard to prove in a test).

## R5. Address rules

- Decision: `https` is always allowed. `http` is allowed for a loopback host (`localhost`, `127.0.0.0/8`, `::1`) or when `allowInsecure` is true. Other schemes are rejected. The base is the location with a trailing "/". Index URL = base + `index.json`. A file location is resolved against the base. The result must have the same scheme, host, and port, and must start with the base path. Else the entry is rejected.
- Rationale: Clarifications. A prefix check after normalization stops "..", encoded dots, and absolute paths.
- Alternatives: A host allow list (more setup).

## R6. Folder source

- Decision: The folder is a full path. Resolve each file location with `Path.GetFullPath` against the folder. Reject when the result is not under the folder. Reject rooted paths and drive letters. Open with a read-only stream and check the length.
- Rationale: FR-006 and the edge case for path escape.
- Alternatives: None simpler.

## R7. Limits

- Decision: Index 5 MB. Gamepack file 100 MB (the existing `MaxRequestBytes`). Signature 1 MB. Read as a stream with a counted limit. Reject early on `Content-Length` over the limit and stop when the count passes the limit. The limits are options with these defaults so tests can use small values.
- Rationale: FR-012.

## R8. Hash check

- Decision: Optional `sha256` as lowercase hex (64 chars). Compare with a case-insensitive fixed-time compare of the hash of the downloaded bytes, before `InstallAsync`.
- Rationale: FR-014. An invalid hash string in the index makes the entry invalid (skipped).

## R9. Version order and "newer"

- Decision: Parse `version` as `System.Version`-like major.minor.patch (same rule the gamepack file uses). Sort versions from new to old. The installed marker compares the registry version for the name by equality. "Newer" is by the same compare. The API also returns `updateAvailable` for each version so the page needs no compare code.
- Rationale: Assumption in the spec. The server is the single place for the rule.
- Alternatives: A compare in the page (two places for one rule).

## R10. Settings and token

- Decision: A new store file `gamepack-catalog.json` in the data root, same pattern as the signature settings store. Routes: `GET` and `PUT /api/gamepacks/catalog/settings`. GET returns `location`, `allowInsecure`, `tokenSet`. PUT takes `location`, `allowInsecure`, and `token` (null = keep, empty = clear, text = set).
- Rationale: Clarification: same settings store style; the token is never returned.
- Note: The token sits in a plain file in the data root, as other instance secrets do. The file is not part of any API export.

## R11. Logging

- Decision: One `LogInformation` for each list and each install with name, version, and outcome code. The token is never an argument. Error text from the catalog is not logged raw; the log uses the reason code and the host only.
- Rationale: FR-019, SC-004.

## R12. Web page

- Decision: New page `GamepacksPage.tsx` with a settings form and a list. The web UI has no router library, so the page is a new navigation area `gamepacks` (path `/gamepacks`, phone `full`) in `src/web-ui/src/types/navigation.ts` and `src/web-ui/src/App.tsx` (area parse, `renderGamepacks`, switch case). The four navigation test files also change (see plan.md). Phone support: full. Update button for each version with `updateAvailable`; label "Install" when nothing is installed. A dry-run check box. After install the page reloads the list.
- Rationale: FR-016, FR-017.

## R13. No caching and no polling

- Decision: No cache, no timer, no hosted service. The page loads the list on open and on a refresh button only.
- Rationale: FR-015 and the clarification.

## R14. Signature file download

- Decision: The signature file uses the same download path as the gamepack file: same host rule, same token rule, same redirect rule, same request time limit. Only the size limit differs (1 MB). A failed signature download gives the same reason codes as a failed file download. An entry with no signature location gives a null signature, and the feature 146 check sees `missing`.
- Rationale: FR-008 and FR-012. One download path means one set of safety rules.
- Alternatives: A separate download path for signatures (two places for the same rules).

## R15. Test for no background use

- Decision: A dependency-injection registration check. It builds the real service registration and walks the constructor graph transitively (all depth, with a visited set) from each `IHostedService`, `BackgroundService`, and timer-type registration. The test fails if any path reaches `GamepackCatalogService`, `IGamepackCatalogSource`, `GamepackCatalogSourceFactory`, or the catalog settings store, directly or not.
- Rationale: FR-015. A one-level check misses an indirect dependency. A text scan proves nothing about the real wiring.
- Alternatives: A source text scan (rejected by the spec); a one-level check (misses indirect paths).

## R16. Page controls

- Decision: The page has a "dry run" check box and a "Refresh" button. When the check box is on, the install button runs a dry run. The page loads the list on open and on Refresh only. The Update button shows for each version with `updateAvailable`; its label is "Install" when no version is installed.
- Rationale: FR-009, FR-015, FR-016. Operator action is the only trigger.
- Alternatives: A separate dry-run button (more controls on a phone screen).

## R17. Limit tests

- Decision: The limits are options, so tests use small values. Three tests cover FR-012: oversize signature file, oversize gamepack file, and a per-request timeout on a file download (a test server that stalls).
- Rationale: FR-012 asks for all three.

No item stays unresolved.
