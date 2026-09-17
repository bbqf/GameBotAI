# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- Detection coordinate units documented in the OpenAPI document (101-detect-coordinate-units-docs, #188)
  - `POST /api/images/detect` match `x`/`y`/`width`/`height` and `bbox.*` are now described as fractions 0..1 of the capture frame's width/height (clamped, not pixels), and `bbox` as repeating the top-level box. `POST /api/images/detect-all` match `x`/`y`/`width`/`height` are described as pixels. Both operation descriptions state the difference and warn against comparing values from the two routes directly.
  - `templateId` (detect) and `imageId` (detect-all) are described as the same reference image identifier. The detect example now shows the top-level coordinates it really returns.
  - Documentation only: response values, field names and status codes are unchanged.
- Queue roster documented in the OpenAPI document (100-queue-roster-openapi-docs, #179)
  - `QueueDetailResponse.entries` now says it is the queue's current roster in run order, always an array and never null, and the queue's own entries rather than its linked template's. It also says that `GET /api/queues/{id}` is how the roster is read (there is no `GET /api/queues/{id}/entries`). It is no longer published as nullable.
  - The `QueueEntryResponse` fields are described: `entryId` for `DELETE /api/queues/{id}/entries/{entryId}`, and `sequenceName` null / `stale` true when the sequence no longer exists. The `POST`/`PUT /api/queues/{id}/entries` operations now point to the read path.
  - `GET /api/queues/{id}/monitor` no longer carries the single-queue summary, description and example it wrongly shared before.
  - Documentation only: routes, response bodies and status codes are unchanged.
- Sequence step nesting rules in the OpenAPI document (099-sequence-nesting-rules-docs, #178)
  - The `SequenceStep` schema and its `body`/`elseBody` properties now state the nesting rules the service enforces: a Loop body may hold Action, If and Break steps but not another Loop, an If branch may hold only Action steps plus Break when the If sits in a Loop body (an If inside an If branch is rejected), and a top-level Break is rejected. These were previously learned only from a 400 on save.
  - Documentation only: validation, its error messages and execution are unchanged.
- Resume queues after a service restart (098-resume-queues-on-restart, #203)
  - New per-queue option `resumeOnServiceStart` ("Resume after service restart" in the queue form), off by default. A queue that has it on and was Running when the service stopped is started again automatically once the service is back up. That covers a graceful restart, an upgrade, a host reboot and a crash.
  - Only queues that were really running come back. An operator stop, a run that completed or failed on its own, and a failure-policy stop all keep the queue Stopped, and queues without the option stay Stopped as before.
  - The resumed run is an ordinary fresh start from the linked template: At Queue Start and Timer entries behave as on any start, and the previous run's self-reschedules, live schedules and daily retries are not restored. One attempt per queue per service start, with each outcome in the service log (events 7300–7304).
  - The service keeps the ids of running queues in `<data>/queue-run-state.json`, written when a run starts and cleared when it ends outside a shutdown.
  - Perf note: one small file write per queue start and per run end, and one read at service start.
- Alternate reference images (097-multi-reference-image-target, #192)
  - A stored reference image can now carry up to 8 alternates: other stored images that also count as a match for it, such as night-lit crops of the same daylight art. Manage them with GET/PUT /api/images/{id}/alternates. PUT replaces the whole list and [] clears it. GET /api/images/{id}/metadata lists them too.
  - Every detection that names the image also scores its alternates, with the same threshold and without any change to the sequence. That covers sequence image conditions, wait-for-image steps, image-anchored taps, image-match triggers, the readiness gate and POST /api/images/detect. One image can therefore cover both day and night renderings, replacing hand-OR'd Break steps.
  - POST /api/images/detect matches gain an additive matchedReferenceId; 	emplateId keeps the requested id. With several references, matches are merged by score, with ties going to the image itself and then to alternates in order, and de-duplicated by overlap. Runtime detections log the alternate that matched.
  - Deleting an alternate never breaks detection: it is skipped with a warning and reported as exists: false. Deleting an image removes its own alternates list. Backups include alternate images and their lists, and restore re-applies them.
  - Unchanged: images without alternates (same scores, never routed through the new matcher), POST /api/images/detect-all, and thresholds.
  - Perf note: an image without alternates costs one extra sidecar-file existence check per detection. With N alternates, detection takes about N+1 single matches: measured 43.8 ms for one reference and 173.2 ms for four on a 1280×720 frame.
- "Before Each Run" queue schedule type (095-before-each-run-schedule, #202)
  - New queue-template schedule type `BeforeEachRun`, shown as "Before Each Run". Its entries run immediately before every timed, live-scheduled or self-rescheduled firing: time-of-day timers and their daily retries, relative timers, live schedules, and self-reschedule Timer / At Queue Start / Once Per Run firings. Each task that wakes a queue therefore starts from a known state, even if something changed during an idle pause.
  - Several firings due at one wake-up share a single pass, which runs before the first of them. Entries run in template order. Once-per-run steps, at-queue-start entries and after-every-step executions never trigger the pass.
  - Accounting matches After Every Step: these runs do not count as executed, a failure is counted and is non-fatal, and the task still runs. The entries appear in the execution log and are listed once in the queue monitor as "Before Each Run".
  - The template API accepts and returns `BeforeEachRun`, case-insensitively. The template editor has a new "Before each run" area.
  - Perf note: a template without Before Each Run entries adds only an empty-list check per firing. Otherwise the cost is the configured sequences' own run time, once per wake-up.
- Sequence time-limit cancellation reported distinctly (094-sequence-time-limit-reporting, #182)
  - A queue firing cut off by its time bound still reads `finalStatus: "failure"`, but its sequence's execution-log entry now also carries `cancellationReason: "sequence_time_limit"` and `timeLimitMs`. The fields appear in the list, detail and subtree responses. A timeout can now be told apart from a sequence that reached its own failure, and that includes a step that swallowed the cancellation. User stops, ordinary failures, successes, ad-hoc runs and older entries carry neither field.
  - `GET /api/sequences/{id}` adds read-only `effectiveWatchdogTimeoutMs`: the sequence's `watchdogTimeoutMs` when set, otherwise the 240000 ms default. It is ignored on writes.
  - The OpenAPI document now describes `watchdogTimeoutMs` (default 240000 ms, minimum 1, maximum 1800000 ms), the effective read-out and the new execution-log fields.
- Duplicate queues (083-duplicate-queues)
  - A new "Duplicate" action on the Queues page creates a copy of an existing queue: cycle-execution/idle-pause settings, linked template, linked game, and the queue's currently loaded entries are all copied over. The only required input is a new name, which must differ from the source queue's current name; the confirm button and the API both reject a resubmitted unchanged name.
  - The dialog also pre-fills the source queue's emulator (serial, instance name, instance index) but — unlike the name — lets it be changed or left as-is, so a duplicate can target a different emulator, which a normal edit does not allow.
  - The duplicate links to the *same* template as the source (no template copy) and is always created stopped with no execution history, regardless of whether the source was running at the time. The source queue itself is never modified.
  - API: `POST /api/queues/{id}/duplicate` with `{ "name": string, "emulatorSerial": string, "emulatorInstanceName"?: string, "emulatorInstanceIndex"?: number }`, returning `201 Created` with the new queue (same shape as `POST /api/queues`).
- Tap-point jitter (058-tap-point-jitter)
  - Every tap and swipe sent to the device now lands within a small random offset of its target instead of always hitting the exact same pixel, making repeated executions look more natural. Each axis offset is drawn independently from `[-radius, +radius]`; swipe start and end points are jittered independently; results are clamped so coordinates are never negative.
  - Applied automatically and centrally in the session input dispatch path, so it covers all origins: authored steps, image-detection-resolved primitive taps, recorder replays, and the raw `POST /sessions/{id}/inputs` API. No per-step opt-in/opt-out and no new authoring/execution UI controls.
  - New configuration parameter `GAMEBOT_TAP_JITTER_RADIUS_PX` (default `5`): `0` disables jitter entirely (pixel-exact input); negative/invalid values fall back to the default. Follows the standard precedence (default → saved config file → environment variable), appears in the generic UI Configuration variables list, and is documented in `ENVIRONMENT.md`.
  - Execution logs and step outcomes now report both the pre-jitter target and the post-jitter executed coordinates: tap details read "Tap targeted (X,Y), executed at (X',Y')" and step outcome payloads gain additive `executedPoint`, `targetSwipe`, and `executedSwipe` fields alongside the existing `resolvedPoint`.

### Changed
- Break step success/failure execution statuses (066-break-step-status)
  - A break that **does not fire** (its condition evaluated false) now shows a distinct, neutral **"No break"** state in the execution logs instead of the old `Skipped` label — clearly signalling that the loop simply continued, and never the alarming red "Failed".
  - A break whose condition **cannot be evaluated** (a runtime error) is now treated exactly like a false condition — a non-influential "No break" — so execution continues and the run no longer fails. This reverses the previous behavior where a break-condition error aborted the run. The same guarantee applies to a loop-level `breakOn` condition on a while block, whose evaluation errors are now guarded.
  - A break that **fires** is reported as a success (fixing a latent miscolor where a fired break could fall through to the red "failure" styling). A non-firing break never marks the enclosing loop, sequence, or run as failed and is excluded from failure counts. No change to break authoring, break firing behavior, or the persisted log format — only the reported *outcome* of a break changes.

### Fixed
- Queue health now reports an Idle Pause (096-idle-pause-health-paused, #199)
  - While a running queue was in an idle pause, `GET /api/queues/{id}` returned `health.paused: false` with a null `pausedAt` and `pauseReason`, even as `/monitor` reported `IdlePause` for the same queue. `health.paused` now covers both kinds of pause. During an idle pause, `pausedAt` is when the hold began, and `pauseReason` reads `idle pause: resumes at HH:mm`.
  - Added `health.pauseKind` (`idle` | `failurePolicy` | null), so a routine idle pause can be told apart from a failure-policy park that needs `POST /api/queues/{id}/resume`. If both were in force at once, the failure-policy pause is reported. Failure-policy pause values are unchanged, and `/resume` still releases only a failure-policy pause.
  - The OpenAPI document now describes `paused`, `pausedAt`, `pauseReason` and `pauseKind`.
  - Perf note: a health read takes at most two extra uncontended locks. The idle-pause poll loop is unchanged.

## [0.7.0] - 2026-06-02

### Added
- Queue execution runtime (051-queue-execution-runtime)
  - Starting a queue now runs it for real: the linked template is loaded server-side, a session is opened to the queue's bound emulator, and the template's sequences run in order — replacing the previous placeholder that only flipped status.
  - "Cycle execution" gains its runtime meaning: when enabled, the run repeats from the first sequence (reusing the template snapshot loaded at start) until stopped or a run-level failure; an empty template completes without busy-looping.
  - Stopping a queue aborts the run promptly, disconnects the emulator session, and records a "stopped manually" entry; the session is disconnected on every end path.
  - Every run writes exactly one terminating queue-run execution-log entry with its stop reason (completed full run / stopped manually / failure). Individual sequence failures are non-fatal — recorded per sequence while the run continues — whereas no resolvable linked template, an unreachable emulator at start, or a lost connection mid-run end the run as a failure.
  - Concurrency: one run per queue (a second start returns 409 `already_running`); queues bound to the same emulator may run concurrently (operator's responsibility).
  - Execution log: a queue run appears as a single top-level entry (`executionType: "queue"`) with its sequences and their steps nested beneath it, reusing the existing hierarchy/grid; the grid shows a "Queue" row type.
  - API: `POST /api/queues/{id}/start` launches the run (200 Running, 404, or 409 `already_running`); `POST /api/queues/{id}/stop` aborts and is a no-op when not running.
  - Internal: extracted the sequence-execution wiring into a reusable `ISequenceExecutionService` shared by the standalone execute endpoint and the queue engine.

### Changed
- Execution logs now reflect what was actually executed (049-execution-logs-hierarchy)
  - The execution-logs list shows only top-level executed entities — stand-alone commands and sequences. Commands invoked as part of a sequence run are no longer separate top-level rows; they are recorded as linked children (kept, not deleted) and nested under their sequence.
  - Top-level rows are now expandable: expanding a sequence reveals its ordered sub-elements (steps, invoked commands, primitive taps, conditions, loops, wait-for-image outcomes) with the same detail available before, and command sub-elements drill into their own primitive outcomes.
  - A running sequence appears as a single in-progress entry (new `running` status) created at start and finalized in place when it completes; the page polls (~2s) while any entry is running so sub-elements update live without a manual reload.
  - API: `GET /api/execution-logs` returns roots-only and each item gains `childCount`; `finalStatus` now includes `running`. New `GET /api/execution-logs/{id}/subtree` returns the nested execution tree.
  - Performance: roots-only listing and subtree fetch are in-memory filters over the existing log store (no extra I/O on the hot path); live updates use a bounded ~2s poll gated on the presence of an in-progress entry (no polling when idle).
  - Historical logs recorded before this change remain viewable as completed leaf rows (no migration required).

### Fixed
- Sequence step command names are now preserved across save/reopen cycles (`001-fix-sequence-step-names`)
  - Reopening a saved sequence restores each step's selected command and user-visible label without reverting to "Select command".
  - Execution-log detail entries now identify both the step label and the command name that ran, making logs operator-readable.
  - When a referenced command is deleted after a sequence was saved, the affected step shows the last-saved command name as unresolved instead of appearing blank; other steps in the same sequence are unaffected.

### Added
- Wait For Image primitive action (001-wait-for-image)
  - Added `WaitForImage` as an authorable step in both command and sequence flows.
  - Added optional `detectionTarget` and `timeoutMs` payload support with default timeout normalization.
  - Added command and sequence runtime behavior that treats `image_detected`, `timeout_elapsed`, and `image_unavailable` as terminal wait outcomes.
  - Extended execution-log detail payloads and UI rendering to show wait parameters and terminal wait exit conditions.
  - Added backend unit/integration/contract coverage and web-ui authoring/service/log rendering coverage for wait-step scenarios.

- Tap Wait-and-Retry Before Execution (036-tap-wait-retry)
  - Primitive tap steps now wait for the target image to appear before tapping, retrying up to a configurable number of times.
  - New environment variables for configuration:
    - `GAMEBOT_TAP_RETRY_COUNT` — maximum retry cycles (default 3; 0 = single check, no retries).
    - `GAMEBOT_TAP_RETRY_PROGRESSION` — multiplier applied to the wait time after each retry (default 1.0 = constant; >1 = exponential backoff).
    - `GAMEBOT_CAPTURE_INTERVAL_MS` — now also used as the base wait time between retry cycles (default 500ms, min 50ms).
  - Retry metadata encoded in `PrimitiveTapStepOutcome.Reason` field (e.g., `detected_after_2_retries`, `detection_failed_after_3_retries`, `cancelled_during_retry_N`).
  - Immediate cancellation support: cancelling during any wait period stops the retry loop within 1ms.
  - Source-generated `[LoggerMessage]` log entries for retry cycle tracing (EventIds 6009–6013).

- Background Screenshot Service (034-background-screenshot-service)
  - Per-session background capture loop runs on a dedicated thread, taking ADB screenshots at a configurable interval (`GAMEBOT_CAPTURE_INTERVAL_MS`, default 500ms, min 50ms).
  - Cached frames available instantly via `BackgroundScreenCaptureService.GetCachedFrame()` — dual-format cache (PNG bytes + Bitmap) so consumers choose their preferred format.
  - All `IScreenSource` consumers now read from the background cache instead of making direct ADB calls.
  - Screenshot endpoint (`GET /api/emulator/screenshot`) serves cached frames with fallback to direct ADB capture during startup.
  - Optional `sessionId` query parameter on screenshot endpoint for explicit session targeting.
  - Capture rate metric (FPS) displayed in the Execution tab for each running session.
  - Automatic lifecycle management: capture loop starts with session, stops when session ends or is evicted.
  - Rolling FPS calculation using a circular buffer of the last 10 capture durations.

### Changed
- Image match evaluation now uses OpenCV `Cv2.MatchTemplate` (CCoeffNormed) via `ITemplateMatcher` instead of brute-force pure-C# NCC. Reduces per-evaluation time from ~5 seconds to ~50 ms for full-screen regions.
- MVC controllers now serialize enums as strings (via `JsonStringEnumConverter`) for consistency with minimal API endpoints.

### Fixed
- Execution tab session status no longer shows "Unknown" due to integer enum serialization mismatch.
- Capture rate display moved to its own labeled line; only shown when data is available.
- Web-ui test warning noise reduced by stabilizing async Configuration test flows and Jest interop settings.

### Security
- Upgraded `jest-environment-jsdom` from 29.7.0 to 30.3.0 to resolve `@tootallnate/once` vulnerability (GHSA-vpq2-c234-7xj6).
- Upgraded `System.Drawing.Common` from 9.0.0 to 10.0.3 across all projects.

- Command Loop Steps (033-command-loops)
  - Count-based loops (`count`): execute body steps exactly N times.
  - While loops (`while`): re-evaluate condition before each iteration; skip body if false on entry.
  - Repeat-until loops (`repeatUntil`): execute body, then check exit condition; always runs at least once.
  - Break step (`Break`): unconditional or conditional early exit from a loop body.
  - `{{iteration}}` placeholder substitution in `CommandId` and action parameters inside loop bodies.
  - Configurable safety limit (`maxIterations`) per loop step and global `GAMEBOT_LOOP_MAX_ITERATIONS` (default 1000).
  - Per-iteration execution log entries (`LoopIterationOutcome`) with break detection.
  - Validation rules: no nested loops, no top-level breaks, no `{{iteration}}` outside loops, commandOutcome back-ref only.
  - Extended `/api/sequences/{id}/validate` endpoint with step-level loop validation.
  - UI visualization: `LoopBlockHeader`, `BreakStepRow`, `LoopBlock` components with "Add Loop" (count/while/repeatUntil) affordance in SequencesPage.

### Tests
- 15 loop validation unit tests, 15 runner loop tests, 9 template substitutor tests, 6 contract round-trip tests, 14 UI component tests.

## [2026-03-02]

### Added
- Execution Logs top-level navigation tab between `Execution` and `Configuration`.
- Execution Logs list page with sortable columns (`timestamp`, `objectName`, `status`) and per-column text filters.
- Readable execution detail view with summary, related objects, snapshot availability, and step outcomes (no raw JSON rendering).
- Responsive behavior for desktop split-pane and phone drill-down flow with preserved list state.
- Timestamp display mode toggle (exact local default, switchable relative) and latest-request-wins coordination under rapid query changes.
- Backend performance integration test for 1,000-log datasets with strict local and CI-relaxed p95 profiles.

### Changed
- Execution log repository query path now uses an in-memory index loaded once and incrementally updated, reducing repeated disk-deserialization overhead on list requests.

### Tests
- Added/updated coverage across unit, integration, contract, and web-ui tests for execution logs query/detail/responsive behavior.

## [2025-12-17]

### Added
- Command Sequences (Phase 1–5)
  - File-backed sequences persisted under `data/commands/sequences`.
  - Minimal API endpoints:
    - `POST /api/sequences` (create)
    - `GET /api/sequences/{id}` (get)
    - `POST /api/sequences/{id}/execute` (execute)
  - Delay handling: fixed `delayMs` and ranged `delayRangeMs { min, max }` with precedence rules (range overrides fixed when present).
  - Gating: per-step `gate { targetId, condition, confidence? }` evaluated with timeout; on timeout the sequence stops and returns `status = "Failed"`.
  - Telemetry: structured per-step logging via `LoggerMessage` delegates (sequence start/end, applied delays, gate decisions, command duration).
  - Tests: unit tests for delay ranges and gating; integration tests covering fixed delays and gating present/absent scenarios.

### Changed
- README updated with a new “Command Sequences” section including endpoints, auth header usage, and PowerShell examples for create/execute.

### Notes
- Authentication is required for all non-health endpoints; set `GAMEBOT_AUTH_TOKEN` or `Service:Auth:Token`.
- Storage root can be overridden via `GAMEBOT_DATA_DIR` or `Service:Storage:Root`.

## [2025-12-02]

### Added
- Image detections: Additive endpoint `POST /images/detect` returning normalized bounding boxes and confidences in [0,1]. Includes unit, integration, and contract tests.
- Operational metrics: `/metrics/process` endpoint exposing `workingSetMB`, `managedMemoryMB`, and `budgetMB`; integration test verifies working set remains under configured budget (T039).

### Release Notes
Image Match Detections feature shipped across stacked phases:
- Phase 1–2: Introduced OpenCvSharp4 dependency, template matcher, IoU/NMS utilities, domain exceptions, timing helper, DI wiring.
- Phase 3: Exposed `POST /images/detect` endpoint with validation, structured logging, and contract + integration tests.
- Phase 4: Backward compatibility validation—existing `/images` endpoints unchanged; immutability and OpenAPI presence tests.
- Phase 5: Normalization guarantees—coordinates and confidences now resolution & scale independent with dedicated unit/integration coverage.
- Phase 6: Performance & hardening—timeouts, metrics (duration/result count + process memory), stress tests, resource safeguards.

Configuration keys (override via env): `Service__Detections__Threshold`, `Service__Detections__MaxResults`, `Service__Detections__TimeoutMs`, `Service__Detections__Overlap`.
Operational guidance: Raise `Threshold` to reduce noise; monitor `/metrics/process` for memory trends and adjust budget or template sizes accordingly.

### Notes
- Backwards compatibility: Existing endpoints and trigger evaluators remain unchanged. Detection feature is additive and does not mutate stored images.

## [2025-11-26]

### Added
- OCR: Tesseract TSV integration
  - Pass `-c tessedit_create_tsv=1` to emit `output.tsv` alongside `output.txt`.
  - Parse TSV into word-level tokens (level=5) with floating-point confidences.
  - Reconstruct text from TSV tokens when `.txt` is not produced.
  - Force `en-US` numeric parsing to avoid locale-specific failures.
- Tests and fixtures
  - TSV fixtures under `tests/TestAssets/Ocr/tsv` and unit tests for header/rows/aggregation/malformed cases.
  - Updated `TesseractProcessOcr` tests to assert TSV args and behavior.
- Persistent reference image storage:
  - Disk-backed `ReferenceImageStore` under `data/images` with atomic PNG writes.
  - Endpoints: `POST /images`, `GET /images/{id}`, `DELETE /images/{id}`.
  - Integration test ensuring persistence across restart.

### Changed
- Confidence calculation now prefers TSV aggregate (scaled 0–1) and falls back to legacy text heuristic only when TSV is missing or invalid.
- Triggers: default `CooldownSeconds` is now 0 (was 60). Added a unit test to lock the default.
- Image trigger flow now supports persisted reference images across service restarts (no re-upload needed).

### Notes
- Backwards compatibility: existing triggers remain unchanged; the cooldown behavior only differs for newly created triggers relying on the default value.
- No persistence schema changes; ENV docs updated for OCR TSV usage.

## [Unreleased] - 2025-11-25

### Added
- Actions, Commands, and Triggers as first-class resources with file-backed repositories.
- Endpoints:
  - `/actions` CRUD and `/sessions/{id}/execute-action` execution.
  - `/commands` CRUD, `/commands/{id}/evaluate-and-execute`, and `/commands/{id}/force-execute`.
  - `/triggers` CRUD, `POST /triggers/{id}/test`, and `POST /triggers/evaluate`.
- Metrics: `/metrics/domain` exposes counts for actions, commands, and triggers.
- Migration script: `scripts/migrate-profiles-to-actions.ps1` to convert legacy profiles to actions/triggers.
- Structured Tesseract invocation logging (debug-level) capturing CLI, stdout/stderr with truncation guard, exit code, elapsed time, and correlation IDs.
- OCR coverage tooling: `tools/coverage/report.ps1` now emits Cobertura-derived summaries, writes `data/coverage/latest.json`, and enforces ≥70% coverage for `GameBot.Domain.Triggers.Evaluators.Tesseract*`.
- Coverage summary API surface: `GET /api/ocr/coverage` plus contract/integration tests so stakeholders can query latest coverage without parsing XML.
- 001-image-storage: Persistent reference image storage under `data/images` with atomic writes, structured LoggerMessage logging for `/images` endpoints, standardized error responses (`invalid_request`, `invalid_image`, `not_found`), and increased test coverage (evaluator edge cases and endpoint error paths). CI stability improvements: isolated storage via `Service__Storage__Root` + `GAMEBOT_DATA_DIR`, persistence test robustness, and evaluator GDI+ OOM fix using `Graphics.DrawImage`.

### Changed
- Configuration precedence clarified and enforced: Environment > Saved file > Defaults. See `ENVIRONMENT.md`.
- OCR: Improved preprocessing and dynamic environment-driven OCR stub; Tesseract integration remains optional but supported.
- README updated with new domain model and migration guidance.
- README + specs quickstart now document OCR logging toggles, coverage script usage, and `/api/ocr/coverage` flows (stale/missing behavior, bearer auth).
- `POST /commands/{id}/evaluate-and-execute` now surfaces `triggerStatus` + `message`, always persists trigger evaluation before dispatching actions, and emits structured telemetry (`TriggerExecuted`, `TriggerSkipped`, `TriggerBypassed`). Integration tests now assert both HTTP metadata and trigger repository state for satisfied, pending, cooldown, and disabled flows; quickstart instructions updated accordingly.

### Removed (Breaking)
- All `/profiles` endpoints and nested trigger routes.
- Background trigger worker; evaluation is now explicit via endpoints or command gating.
- Legacy session execute path `/sessions/{id}/execute`.

### Migration
1. Backup the `data/` directory.
2. Run the migration script:
   - Dry run: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DryRun`
   - Convert and delete originals: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DeleteOriginal`
3. Update clients to use `/actions`, `/triggers`, and `/commands` endpoints.

### Notes
- OpenAPI spec regenerated for the new endpoints.
- Full test suite green after refactor with additional tests (cycle detection, metrics).
