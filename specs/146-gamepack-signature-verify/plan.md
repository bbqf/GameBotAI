# Implementation Plan: Verify the signature of a gamepack before install

**Branch**: `146-gamepack-signature-verify` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/146-gamepack-signature-verify/spec.md` (with the corrected clarifications and FR-001 to FR-017)

## Summary

A gamepack can carry one detached signature. The signature file lists the hash of the manifest and the hash of each content file. It holds one ECDSA P-256 signature over the canonical form of that list. The instance holds a policy (`required`, `optional`, `off`) and a list of trusted public keys. Both stay on disk and change without a restart.

On install, the service parses the gamepack first (the existing parse error stays). It then checks the signature before the validator and before any write. The first fault in this order gives the status: `missing`, `invalid`, `content_mismatch`, `untrusted`. Under `required`, a status other than `valid` gives HTTP 422 and no change. Under `optional`, the install runs as before and the result reports the status. Under `off`, the route never loads the signature part and no verifier runs.

Technical approach (details in [research.md](research.md)):

- Algorithm: ECDSA P-256 with SHA-256 from `System.Security.Cryptography`. No new package.
- The signature file holds the public key of the signer. The instance uses it only to test the crypto (`invalid`). Trust is only "the fingerprint is on the trusted list" (FR-012).
- A canonical JSON writer gives equal bytes for equal content (sorted keys, fixed string escapes, fixed number form).
- The signature travels as a second part of a `multipart/form-data` install request. The existing raw JSON request stays valid and means "no signature".
- Policy and keys live in one JSON file in the data root. The store reads it on each install (no restart, SC-005) and writes it atomically.
- `GamepackService` gets one optional dependency (the signature checker) and one optional trailing parameter. Existing tests and the DI wiring stay valid (FR-015).
- Each check writes one application log message with the status and the policy (FR-016).

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution)
**Primary Dependencies**: ASP.NET Core minimal endpoints, `System.Text.Json`, `System.Security.Cryptography`, Swashbuckle (schema filter). No new package.
**Storage**: One new JSON file `gamepack-signature.json` in the data root (policy and trusted keys). No other store.
**Testing**: xUnit. Unit tests (canonicalizer, hasher, verifier, status order, settings store, log record), contract tests (routes, status codes, OpenAPI, no restart, token rule), integration tests (real stores, state compare, rollback and list not checked, 50 MB timing).
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service (backend only; no web UI change)
**Performance Goals**: The check adds less than 2 seconds for a 50 MB gamepack (SC-007). The hash pass is one linear read.
**Constraints**: The install request limit stays 100 MB. Nothing changes before the check ends. Under `off`, zero signature reads. No key secret in any log or response.
**Scale/Scope**: Tens of trusted keys at most. One signature per gamepack.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists. Status: acknowledged. The final task runs the full build and test suites.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI). Status: pass.

| Principle | Result | Note |
|-----------|--------|------|
| I. Code quality | Pass | Small classes with one job. Method names use CamelCase with no underscores. `Program.cs` stays thin: the wiring goes in the existing service-setup file. Endpoint handlers are named methods (taint analyzers). |
| II. Testing | Pass | A test for each requirement and each success criterion. See "Test Strategy and Task Notes". |
| III. User experience | Pass | No web UI change. Phone check: not applicable. API text and OpenAPI descriptions are in STE. Rejection text names the status and gives no forger help. |
| IV. Performance | Pass | Goal stated above (SC-007) with a timing test. |
| V. Living documentation | Pass | Tasks update `docs/gamepacks.md` (format, signing, keys, policies; FR-017), `docs/architecture.md` (API surface, "Last reviewed"), `specs/STATUS.md`, `CHANGELOG.md`, and the spec Status. |
| VI. STE | Pass | All artifacts, messages, and comments in STE. |
| Version bump | Pass | The version bump task changes `patch` before the push. |

Post-design re-check (after Phase 1): no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/146-gamepack-signature-verify/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── signature-file.md           # Phase 1 output: file format and signed bytes
│   └── gamepack-signature-api.md   # Phase 1 output: routes and install changes
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Gamepacks/
│       ├── GamepackService.cs                  # optional checker dependency; optional trailing signatureText
│       └── Signing/                            # new
│           ├── SignaturePolicy.cs              # enum required, optional, off
│           ├── SignatureStatus.cs              # enum + snake_case names
│           ├── TrustedKey.cs                   # label, fingerprint, public key
│           ├── GamepackSignatureSettings.cs    # policy + keys
│           ├── IGamepackSignatureSettingsStore.cs
│           ├── GamepackSignatureSettingsStore.cs   # JSON file, atomic write, read on each call
│           ├── CanonicalJson.cs                # fixed form for keys, strings, numbers
│           ├── GamepackContentHasher.cs        # manifest and content file hashes
│           ├── SignatureFile.cs                # parsed signature file
│           ├── GamepackSignatureVerifier.cs    # status order, ECDSA check
│           └── GamepackSignatureChecker.cs     # policy gate, log message, wouldReject
└── GameBot.Service/
    ├── ApiRoutes.cs                            # add GamepackSignature constant
    ├── GameBotServiceSetup.cs                  # existing service-setup file: call the new registration
    ├── Endpoints/
    │   ├── GamepackServiceCollectionExtensions.cs   # register store, verifier, checker before GamepackService
    │   ├── GamepacksEndpoints.cs               # multipart read, signature result, 422
    │   ├── GamepackSignatureEndpoints.cs       # new: policy and key routes
    │   └── Dto/
    │       ├── GamepackDtos.cs                 # add signature block to install response
    │       └── GamepackSignatureDtos.cs        # new: settings, key, status, dry-run DTOs
    └── Swagger/GamepackSchemaFilter.cs         # descriptions for the new types

tests/
├── unit/Gamepacks/Signing/
├── contract/
└── integration/

docs/gamepacks.md                               # signature section (FR-017)
```

**Structure Decision**: Domain logic in `GameBot.Domain/Gamepacks/Signing/`, HTTP in `GameBot.Service/Endpoints/`. The DTOs go in the existing `Endpoints/Dto/` folder and exist before the routes that use them. The new services are registered in `GamepackServiceCollectionExtensions.AddGamepacks` (called from `GameBotServiceSetup.cs`) before `GamepackService`.

## Test Strategy and Task Notes

- Each requirement has one test owner. The map is in [quickstart.md](quickstart.md). The `off` checks (FR-009, SC-004) are owned by User Story 3 only.
- Order of work: DTOs and enums first, then store, canonicalizer, hasher, verifier, checker, service change, DI wiring, routes.
- FR-003 and the edge cases: unit tests for extra file, missing file, changed byte, empty signature, unknown format.
- Status order (clarification): a unit test with a gamepack that has several faults at once.
- SC-004: the route test uses a stream wrapper that counts reads of the signature part. Under `off` the count is zero and a verifier spy gets zero calls.
- SC-002: a snapshot of the gamepack registry, sequences, commands, images, and the settings file before and after a rejected install must be equal.
- SC-005: the trust contract test adds a key and installs, then changes the policy and installs, on one host with no restart.
- SC-006: two unrelated keys, both trusted, same content: both give `valid`. The label is changed and the status does not change.
- SC-007: a 50 MB gamepack timing test. It does not run in the default CI set if the runner is too slow (mark as a trait).
- FR-016: a log capture test reads the record. It holds status and policy and not the key text. Contract tests need the logging gate component open (see test-harness notes).
- Clarification A2: a test checks each new settings route against the existing API token rule.
- Clarification A12: tests show that rollback and list run no check under `required`.
- Clarification A4: an unparsable gamepack under `required` returns the existing parse error.
- Contract tests share a bin data directory. Use a temp data root for each test host.
- Keep the existing gamepack tests unchanged. They build `GamepackService` without a checker.
- Docs tasks (FR-017) and the patch bump task come last.

## Complexity Tracking

No constitution violation. This table is empty.
