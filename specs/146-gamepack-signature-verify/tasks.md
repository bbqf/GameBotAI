# Tasks: Verify the signature of a gamepack before install

**Input**: Design documents in `C:\src\GameBot\specs\146-gamepack-signature-verify\` (plan.md, spec.md, data-model.md, research.md, quickstart.md, contracts/)
**Tests**: Requested by the spec (each requirement has one test owner; see the test map in quickstart.md).

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- [P] means the task can run in parallel (different files, no open dependency).
- Paths are relative to `C:\src\GameBot\`.
- Test folders: `tests/unit/Gamepacks/Signing/`, `tests/contract/`, `tests/integration/`.
- Contract tests use a temp data root for each host. Open the logging gate component in log capture tests.

---

## Phase 1: Setup

- [X] T001 Create the folder `src/GameBot.Domain/Gamepacks/Signing/` and the test folder `tests/unit/Gamepacks/Signing/`. Read `src/GameBot.Domain/Gamepacks/GamepackService.cs` and `src/GameBot.Service/Endpoints/GamepackServiceCollectionExtensions.cs` to confirm the current constructor and wiring.

---

## Phase 2: Foundational (blocks all user stories)

- [X] T002 [P] Add enums `SignaturePolicy` (required, optional, off) and `SignatureStatus` (valid, missing, invalid, untrusted, content_mismatch, not_checked) with snake_case JSON names in `src/GameBot.Domain/Gamepacks/Signing/SignaturePolicy.cs` and `src/GameBot.Domain/Gamepacks/Signing/SignatureStatus.cs`.
- [X] T003 [P] Add `TrustedKey` (label, fingerprint, publicKey) in `src/GameBot.Domain/Gamepacks/Signing/TrustedKey.cs` and `GamepackSignatureSettings` (policy default `off`, keys) in `src/GameBot.Domain/Gamepacks/Signing/GamepackSignatureSettings.cs`.
- [X] T004 [P] Add the DTOs (settings, key, key request, status block with `status`, `policy`, `wouldReject`) in `src/GameBot.Service/Endpoints/Dto/GamepackSignatureDtos.cs`. Add the `signature` block to the install response in `src/GameBot.Service/Endpoints/Dto/GamepackDtos.cs`. The DTOs must exist before the routes that use them.
- [X] T005 [P] Add the route constant `GamepackSignature` (`ApiRoutes.Gamepacks + "/signature"`) in `src/GameBot.Service/ApiRoutes.cs`.
- [X] T006 [P] Implement `CanonicalJson` (sorted keys, fixed string escapes, fixed number form) in `src/GameBot.Domain/Gamepacks/Signing/CanonicalJson.cs`.
- [X] T007 [P] Write unit tests for `CanonicalJson` (key order, string escapes, number form, equal content gives equal bytes; Clarification A11) in `tests/unit/Gamepacks/Signing/CanonicalJsonTests.cs`.
- [X] T008 Implement `IGamepackSignatureSettingsStore` and `GamepackSignatureSettingsStore` in `src/GameBot.Domain/Gamepacks/Signing/`. The store reads `gamepack-signature.json` in the data root on each call, writes through a temp file and atomic replace, validates a P-256 SPKI key on add, and computes the fingerprint (SHA-256 of SPKI, lower case hex). Depends on T002, T003.
- [X] T009 [P] Write unit tests for the settings store (default `off` when file absent, add, list, remove, duplicate fingerprint, bad key rejected and nothing saved, reload after "restart", FR-004, FR-013) in `tests/unit/Gamepacks/Signing/GamepackSignatureSettingsStoreTests.cs`.
- [X] T010 [P] Implement `SignatureFile` (parse and validate `format`, `algorithm`, `publicKey`, `manifestSha256`, `files`, `signature`) in `src/GameBot.Domain/Gamepacks/Signing/SignatureFile.cs`. An empty or unknown-format text gives a parse failure that maps to `invalid`.
- [X] T011 Implement `GamepackContentHasher` (hash of the canonical manifest and of each sequence, command, and image) in `src/GameBot.Domain/Gamepacks/Signing/GamepackContentHasher.cs`. Depends on T006.
- [X] T012 Implement `GamepackSignatureVerifier` in `src/GameBot.Domain/Gamepacks/Signing/GamepackSignatureVerifier.cs`. Status order: `missing`, `invalid`, `content_mismatch`, `untrusted`. Use ECDSA P-256 with SHA-256 over the canonical `{ format, manifestSha256, files }` bytes. Trust is only "the fingerprint is on the trusted list" (FR-012). Depends on T008, T010, T011.
- [X] T013 [P] Write unit tests for the hasher and the verifier in `tests/unit/Gamepacks/Signing/GamepackSignatureVerifierTests.cs`: valid; extra file, missing file, changed byte (FR-001, FR-003); empty signature and unknown format; a gamepack with many faults at once gives the status order (FR-010); two unrelated trusted keys both give `valid` and a label change does not change the status (SC-006, FR-005, FR-012); same input twice gives the same status.
- [X] T014 Implement `GamepackSignatureChecker` in `src/GameBot.Domain/Gamepacks/Signing/GamepackSignatureChecker.cs`. It reads the policy, runs the verifier only when the policy is not `off`, returns a `SignatureCheckResult` (`status`, `policy`, `wouldReject`; `wouldReject` is true only for `required` with a status other than `valid`), and writes one application log message with status and policy and no key text (FR-016). Depends on T012.
- [X] T015 [P] Write unit tests for the checker, including the log record (holds status and policy, holds no key text; FR-016) and `wouldReject` for all three policies, in `tests/unit/Gamepacks/Signing/GamepackSignatureCheckerTests.cs`.
- [X] T016 Register the settings store, the verifier, and the checker in `src/GameBot.Service/Endpoints/GamepackServiceCollectionExtensions.cs` (`AddGamepacks`), before `GamepackService`. Confirm `src/GameBot.Service/GameBotServiceSetup.cs` calls `AddGamepacks`. Depends on T008, T012, T014. This task MUST finish before T017.
- [X] T017 Change the `GamepackService` constructor in `src/GameBot.Domain/Gamepacks/GamepackService.cs`: add one optional checker dependency and one optional trailing `signatureText` parameter on install. Run the signature check after parse and before the validator and before any write. Return a rejection result when `required` and the status is not `valid`. Existing gamepack tests that build the service without a checker MUST compile and pass unchanged (FR-015). Depends on T016.

**Checkpoint**: The domain check works and `GamepackService` accepts it. No route sends a signature yet.

---

## Phase 3: User Story 1 - Reject a changed gamepack (P1)

**Goal**: Under `required`, a gamepack with a status other than `valid` gets HTTP 422 and no change.
**Independent test**: Set `required` and one trusted key. A signed gamepack installs. The four bad cases (missing, invalid, untrusted, changed content) give 422 and the instance state is the same.

- [X] T018 [US1] Change the install route in `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs` (named handler method): accept `application/json` (no signature) and `multipart/form-data` (parts `gamepack` and `signature`). Read the policy first. Under `off`, the route never loads the `signature` part and passes no signature text to the service (FR-009, Clarification F1). Parse errors keep the existing 400 (A4). Return 422 with `valid: false`, `result: "signature_rejected"`, problem code `signature_<status>`, and text that names the status only (FR-011). Add the `signature` block to the 200 response. Depends on T017, T004. The test for the `off` skip is owned by US3 (T030).
- [X] T019 [P] [US1] Update `src/GameBot.Service/Swagger/GamepackSchemaFilter.cs` and the route summaries with STE descriptions for the install changes and the new types (snake_case status names).
- [X] T020 [US1] Write contract tests in `tests/contract/GamepackSignatureRequiredContractTests.cs`: a valid signed gamepack installs and reports `valid` (SC-001); each of the four bad cases gives 422 with the right problem code (quickstart table); the rejection text has no hash, key, or offset (FR-011); an unparsable gamepack under `required` gives the existing parse error (A4); rollback and list run no signature check under `required` (A12, FR-015).
- [X] T021 [US1] Write an integration test in `tests/integration/GamepackSignatureRejectIntegrationTests.cs` with real stores: take a snapshot of the gamepack registry, sequences, commands, images, and the settings file before and after each rejected install. The snapshots MUST be equal (FR-002, FR-007, SC-002).

**Checkpoint**: User Story 1 works alone, with keys and policy set directly in the settings store.

---

## Phase 4: User Story 2 - Report the status without blocking (P2)

**Goal**: Under `optional`, every gamepack installs and the result shows the status. A dry run reports whether the policy would reject.
**Independent test**: Under `optional`, the five cases install and each shows its own status.

- [X] T022 [US2] Write contract tests in `tests/contract/GamepackSignatureOptionalContractTests.cs`: under `optional`, valid, missing, invalid, untrusted, and content-mismatch gamepacks all install (200) and show the matching status (FR-008, SC-003). The install behavior equals the behavior without this feature.
- [X] T023 [US2] Add the dry-run output to the install route in `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs`: `POST /api/gamepacks/install?dryRun=true` returns 200 with `signature: { status, policy, wouldReject }`. `wouldReject` is true only under `required` with a status other than `valid`. It is false under `optional` and `off`. The dry run changes no state and runs no extra "check and fix" step (FR-014, Clarification U1). Example: under `required`, a bad signature gives `"wouldReject": true`.
- [X] T024 [US2] Write contract tests for the dry run in `tests/contract/GamepackSignatureDryRunContractTests.cs`: `wouldReject` for all three policies and for `valid` and bad statuses; a state compare shows no change (FR-014).

**Checkpoint**: Stories 1 and 2 work.

---

## Phase 5: User Story 3 - Configure the policy and the trusted keys (P2)

**Goal**: The operator changes the policy and the keys through the API. The next install uses them with no restart. Under `off`, no signature is read.
**Independent test**: Change the policy and keys by the routes. The next install uses the new values. Under `off`, the signature read count is zero.

- [X] T025 [US3] Implement the routes in `src/GameBot.Service/Endpoints/GamepackSignatureEndpoints.cs` (named handler methods): `GET` and `PUT` `/api/gamepacks/signature/policy` (400 for an unknown value), `GET` and `POST` `/api/gamepacks/signature/keys` (201, 400, 409; the list has no public key text), `DELETE` `/api/gamepacks/signature/keys/{fingerprint}` (204, 404). Map them from the existing endpoint setup. Add STE summaries and descriptions. Depends on T008, T004, T005.
- [X] T026 [P] [US3] Write contract tests for the policy routes in `tests/contract/GamepackSignaturePolicyContractTests.cs`: default `off`, set and get, 400 for an unknown value.
- [X] T027 [P] [US3] Write contract tests for the key routes in `tests/contract/GamepackSignatureKeysContractTests.cs`: add (201), list (no public key text), duplicate (409), bad key and bad label (400, nothing saved), remove (204), unknown fingerprint (404).
- [X] T028 [P] [US3] Write a contract test for the token rule on each new settings route in `tests/contract/GamepackSignatureTokenContractTests.cs` (Clarification A2).
- [X] T029 [US3] Write the trust contract test in `tests/contract/GamepackSignatureTrustContractTests.cs` with these named cases on one host with no restart (SC-005): `AddKeyThenInstall_UsesNewKeyWithoutRestart` (install is `untrusted` and rejected, add the key by `POST`, install the same gamepack again and it is `valid`); `ChangePolicyThenInstall_UsesNewPolicyWithoutRestart`; `RemoveKeyThenInstall_RejectsAsUntrusted`; no key under `required` rejects a signed gamepack; two trusted keys, either one gives `valid` (SC-006, FR-005, FR-012).
- [X] T030 [US3] Write the `off` contract test in `tests/contract/GamepackSignatureOffContractTests.cs` (owner of FR-009 and SC-004): use a stream wrapper that counts reads of the `signature` part and a verifier spy. Under `off` the read count is zero, the spy gets zero calls, the install follows the existing behavior, and the result shows `not_checked`. A new instance with no setting uses `off`. A valid gamepack installs under all three policies (SC-001).
- [X] T031 [US3] Write a restart integration test in `tests/integration/GamepackSignatureSettingsPersistenceTests.cs`: new store and host on the same data root keep the policy and the keys (FR-013).

**Checkpoint**: All three stories work.

---

## Phase 6: Polish and cross-cutting

- [X] T032 [P] Write the 50 MB timing test in `tests/integration/GamepackSignatureTimingTests.cs`. The check adds less than 2 seconds (SC-007). Mark it with a trait so the default CI set can skip it if the runner is too slow.
- [X] T033 [P] Update `docs/gamepacks.md` with the signature format, how to sign a gamepack, how to add a trusted key, and the three policies (FR-017). Use STE.
- [X] T034 [P] Update `docs/architecture.md` (API surface and "Last reviewed"), `specs/STATUS.md`, `CHANGELOG.md`, and set the Status in `specs/146-gamepack-signature-verify/spec.md`. Use STE.
- [X] T035 Run the full build and the full test suites (unit, contract, integration). Fix all failures. Confirm the existing gamepack tests pass unchanged (FR-015). Run the quickstart steps by hand against a test host.
- [X] T036 Bump the version in `installer/versioning/version.override.json` as CLAUDE.md says: run `git fetch origin master`, compare with `origin/master`, set `patch`, `updatedBy`, and `updatedAtUtc`. Do this last, before the push.

---

## Dependencies and order

- Phase 1, then Phase 2, then Phases 3 to 5, then Phase 6.
- In Phase 2: T002 and T003 first. T008 needs T002 and T003. T012 needs T008, T010, T011. T014 needs T012. T016 (service registration) comes before T017 (constructor change). T017 ends Phase 2.
- US1 (T018) needs T017. US2 builds on the route from T018. US3 routes (T025) need only Phase 2, but T029 and T030 need T018.
- The `off` skip is in T018. Its test is T030 (US3).

## Parallel examples

- Phase 2: T002, T003, T004, T005, T006, T007 together. Then T009, T010, T013 and T015 beside their code tasks.
- Phase 5: T026, T027, T028 together after T025.
- Phase 6: T032, T033, T034 together.

## Implementation strategy

- MVP: Phases 1 to 3 (User Story 1). Set the policy and keys through the settings store in tests.
- Then add US2 (report and dry run), then US3 (settings routes and `off` proof), then polish.

## Summary

- Total tasks: 36
- Phase 1 Setup: 1; Phase 2 Foundational: 16; US1: 4; US2: 3; US3: 7; Polish: 5
