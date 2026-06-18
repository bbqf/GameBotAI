# Phase 0 Research: Relative-Time Sequence Scheduling

All open questions from the spec's deferred/low-impact list and from grounding against the existing code are resolved below.

## D1 — How to model "relative" alongside the existing time-of-day timer

- **Decision**: Add a sibling property `TimerRelativeOffset` (`TimeSpan?`) to `QueueTemplateEntry`, next to the existing `TimerTimeOfDay` (`TimeOnly?`). The timer "mode" is **inferred**: a `Timer` entry with `TimerRelativeOffset != null` is a relative timer; otherwise it is a time-of-day timer (existing behavior). Exactly one of the two MUST be set for a `Timer` entry — enforced at the API validation layer (FR-003).
- **Rationale**: Purely additive to the persisted shape (existing entries have only `TimerTimeOfDay`), so it is backward compatible (SC-008) with no migration. Mirrors the existing nullable-property pattern already used for `TimerTimeOfDay`. Avoids a new discriminator enum that would require touching every existing persisted entry.
- **Alternatives considered**:
  - *New `TimerMode` enum discriminator on the entry* — more explicit but adds a field to migrate and an extra invalid-state combination to guard; rejected for minimalism. The "exactly one set" invariant gives the same guarantee.
  - *New top-level `ScheduleType` value (e.g., `RelativeTimer`)* — rejected; the spec is explicit that relative offset is a way to express the existing `Timer` type, not a new schedule type (Assumptions: "Builds on feature 053").

## D2 — Anchor and fire-once bookkeeping for template relative timers

- **Decision**: Capture a run-start timestamp once at the top of `RunAsync` (`runStartedAt = _timeProvider.GetLocalNow()`), persisted on the `QueueRunHandle`. At each iteration boundary, a relative timer is due when `_timeProvider.GetLocalNow() - runStartedAt >= TimerRelativeOffset`. Track fired state in a `HashSet<int>` of relative-timer indices (per run), declared outside the cycle loop so it persists across cycles → fires once per run (FR-005).
- **Rationale**: Matches the existing time-of-day timer bookkeeping (a per-run dictionary declared outside the `do/while`), so the two evaluation paths sit side by side and read consistently. Run start (not cycle start) is the anchor per the spec Assumptions ("for cyclic queues the offset is still measured from the overall run start").
- **Alternatives considered**: Re-anchoring per cycle — rejected; contradicts the clarified spec and the fire-once-per-run requirement.

## D3 — Deterministic time for tests (`TimeProvider`)

- **Decision**: Inject `System.TimeProvider` (built-in, .NET 8+) into `QueueExecutionService`, defaulting to `TimeProvider.System`. Replace direct `DateTime.Now` reads (existing time-of-day timer evaluation + new relative evaluation) with `_timeProvider.GetLocalNow()`. Tests use a tiny in-repo `FakeTimeProvider` stub (subclass of `TimeProvider` with a settable/advanceable now) to advance time deterministically — no external test package, honoring the "no new external packages" constraint.
- **Rationale**: Elapsed-offset logic is untestable against the wall clock without flakiness; `TimeProvider` is the framework-standard seam, requires no new external dependency, and incidentally makes the existing (currently time-dependent) timer tests deterministic — a net quality improvement.
- **Alternatives considered**: A bespoke `IClock` interface — rejected; `TimeProvider` is the platform-blessed equivalent and avoids inventing project-specific abstractions. `Thread.Sleep`-based tests — rejected; slow and flaky, violates Testing Standards (<1s, deterministic).

## D4 — Live-schedule injection into a running run

- **Decision**: Add `LiveScheduleOutcome ScheduleRelative(string queueId, string sequenceId, TimeSpan offset)` to `IQueueExecutionService`. It looks up the `QueueRunHandle` in the existing `_runs` `ConcurrentDictionary`; if absent → `NotRunning`. Otherwise it upserts `sequenceId -> (now + offset)` into a new `PendingLiveSchedules` `ConcurrentDictionary<string,DateTimeOffset>` on the handle (upsert = most-recent-wins per sequence, FR-011). The run loop, at each iteration boundary, snapshots due entries (`fireAt <= now`), fires each via the existing `RunOneSequenceAsync`, and removes them (fires once, FR-009/FR-010). The dict lives on the handle and is dropped when the run ends → ephemeral (FR-008).
- **Rationale**: Reuses the single existing owner of running queues; no new background service, no persistence, naturally per-run. `ConcurrentDictionary` is safe for endpoint-thread writes concurrent with run-loop reads/removes; the run only reads at iteration boundaries so there is no mid-step interruption (FR-014).
- **Alternatives considered**:
  - *A persistent/standalone scheduler service* — rejected; over-engineered for ephemeral, per-run schedules and would risk persistence leaking (violates FR-008).
  - *Keying pending schedules by entry id* — rejected; live schedules may target any library sequence not present as an entry (FR-013), so the natural key is the sequence id.

## D5 — Counting relative/live firings toward the run total

- **Decision**: Increment the run's `executed` counter for each relative-timer firing and each live firing (FR-016a). Leave time-of-day timer and every-step firings uncounted (unchanged). Run termination remains governed solely by completing the `OncePerRun` entries (the `do/while` condition is untouched).
- **Rationale**: Directly encodes the clarification (Q2 → "Yes, counts toward the run total"). Because termination is independent of `executed`, counting affects only reported totals/summary (SC-010) and cannot cause non-termination.
- **Alternatives considered**: Not counting (consistent with other timers) — rejected; explicitly overridden by the user's clarification.

## D6 — Offset wire format and bounds

- **Decision**: Represent the offset on the wire as a `"HH:mm:ss"` string (e.g., `"00:10:00"` for 10 min), parsed with `TimeSpan.TryParseExact`/`TryParse` to a `TimeSpan`. Validation: must parse, must be `>= TimeSpan.Zero`, and `<= 24:00:00` as a sane upper bound (the deferred "max offset" detail; out-of-range → 400). The UI collects hours/minutes/seconds inputs and composes the string. `TimeSpan?` persists as the same `"HH:mm:ss"` string in JSON.
- **Rationale**: `"HH:mm:ss"` is the natural System.Text.Json `TimeSpan` representation (no custom converter), human-readable in stored templates, and trivially round-trips. A 24h cap matches realistic run durations without constraining the literal use case (10 min).
- **Alternatives considered**: Integer seconds — workable but less self-describing in stored JSON and in the API; rejected for readability. ISO-8601 duration (`PT10M`) — rejected; not the default TimeSpan format, would need a custom converter.

## D7 — Live-schedule endpoint placement, validation, and errors

- **Decision**: `POST /api/queues/{id}/live-schedule` with body `{ "sequenceId": "...", "offset": "00:10:00" }`. Validation order: (1) queue exists → else 404; (2) offset well-formed and non-negative/in-range → else 400 `invalid_request`; (3) sequence exists in `ISequenceRepository` → else 404 `not_found`; (4) `ScheduleRelative` returns `NotRunning` → 409 `not_running`. On success → 200 with `{ sequenceId, offset, expectedFireAt }`. Reuses the existing `{ error: { code, message, hint } }` envelope.
- **Rationale**: Co-locates with the other `/api/queues/{id}/...` run-control actions (`/start`, `/stop`), reuses existing error shape and DI (`IQueueRepository`, `ISequenceRepository`, `IQueueExecutionService`). Status codes mirror existing conventions (`409 already_running` → `409 not_running`).
- **Alternatives considered**: Putting it under `/api/queue-templates` — rejected; the action is against a *running queue*, not a template.

## D8 — UI surfaces

- **Decision**: In `QueueEntryList.tsx`, when an entry's schedule type is `Timer`, show a mode toggle (Time of day | Relative). For Relative, render minutes/seconds (and optional hours) number inputs and a distinguishing badge ("Timer · relative"). In `QueuesPage.tsx` running-queue view, add a per-sequence "Schedule in" control (mm:ss) that calls `liveScheduleSequence` and shows a pending indicator with the expected fire time until it fires. Client-side validation rejects negative/blank before submit (FR-021).
- **Rationale**: Extends the existing entry-row schedule controls and badge pattern already present for Every Step / Timer; the running-queue control mirrors existing queue action buttons. Keeps both authoring (template) and live (runtime) surfaces consistent with current UX.
- **Alternatives considered**: A separate modal for live scheduling — heavier; an inline control is faster for the "under 1 minute" target (SC-009). Can be revisited if entry density makes inline cramped.

## Resolved unknowns summary

| Unknown (from spec/grounding) | Resolution |
|---|---|
| Max offset / precision (deferred) | `"HH:mm:ss"`, seconds precision, `0 .. 24:00:00` (D6) |
| Mode discrimination | Inferred from which timer field is set; "exactly one" enforced (D1) |
| Deterministic time in tests | Inject `TimeProvider` (D3) |
| Live-schedule transport | New `POST /api/queues/{id}/live-schedule` (D7) |
| Live-schedule storage | In-memory `ConcurrentDictionary` on `QueueRunHandle` (D4) |
| Counting semantics | Count relative + live firings in `executed`; termination unchanged (D5) |
