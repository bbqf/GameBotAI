# Research: 058 Tap-Point Jitter

## R-001: Jitter Injection Point

**Decision**: Apply jitter inside `SessionManager.SendInputsAsync` (`src/GameBot.Emulator/Session/SessionManager.cs`) as a **normalization pass at the top of the method** — after the session lookup but **before** the `_useAdb`/`DeviceSerial` branch: materialize the `actions` enumerable once, then for each action with `Type == "tap"` jitter `Args["x"]`/`["y"]`, and for `Type == "swipe"` independently jitter `Args["x1"]/["y1"]` and `["x2"]/["y2"]`, writing the jittered values back into the same `Args` dictionary entries before they are used for the ADB call.

**Rationale**: `SendInputsAsync` is the single chokepoint through which every tap and swipe reaches the device, regardless of origin (directly authored steps, image-detection-resolved primitive taps dispatched as zero-length swipes, recorder-replayed actions, or the raw `POST /sessions/{id}/inputs` API). Applying jitter here satisfies FR-012/SC-003 ("100% of tap and swipe actions ... regardless of how their target coordinates were produced") without touching `ISessionManager`'s public signature or any call site. `InputAction.Args` is a `Dictionary<string,object>` (reference type), so mutating it in `SendInputsAsync` is visible to the caller (`CommandExecutor`) after the `await SendInputsAsync(...)` call returns, enabling FR-013 (reporting both pre-jitter target and post-jitter executed coordinates) without changing the method's return type.

The normalization pass MUST run before the ADB-mode branch, not inside it: when `GAMEBOT_USE_ADB=false` or no device serial is bound, `SendInputsAsync` returns via a stub path that never iterates the actions' `Args`. Jittering inside the ADB branch would (a) make stub-mode behaviour diverge from real-mode behaviour (caller-visible `ExecutedPoint` would silently equal the target), and (b) make the jitter path untestable in CI, where tests run with `GAMEBOT_USE_ADB=false` and cannot bind a device. Normalizing up front keeps both modes consistent and lets `SessionManagerJitterTests` exercise the real jitter logic in stub mode.

**Alternatives considered**:
- Apply jitter in `CommandExecutor` before constructing `InputAction`s — would require duplicating the jitter call at every `InputAction` construction site (primitive tap-as-swipe, explicit swipe step, any future step types) and would miss any future caller of `SendInputsAsync` that bypasses `CommandExecutor`. Centralizing in `SessionManager` is more robust and matches "regardless of origin".
- Apply jitter in `AdbClient.TapAsync`/`SwipeAsync` — those are lower-level wrappers reused for retries; jittering there would re-jitter on every ADB retry of the *same* logical action, producing a different point per retry attempt, which is undesirable (a single logical tap should have one target point per dispatch).

## R-002: RNG Approach and Seed Uniqueness

**Decision**: Reuse the existing non-cryptographic LCG pattern from `SequenceRunner.GetAppliedDelay`/`SampleInterStepDelay` (seeded from `DateTime.UtcNow.Ticks`), but combine the tick-based seed with a process-wide `Interlocked.Increment`-based counter before the LCG step. Implement as a small static helper, `CoordinateJitter`, in `GameBot.Domain.Services`.

**Rationale**: The constitution and existing code establish that non-cryptographic randomness is acceptable for non-security-sensitive timing/positioning (avoids CA5394). However, a swipe's start and end points (and a tap's X and Y) are jittered via back-to-back calls that can occur within the same clock tick on Windows (`DateTime.UtcNow` resolution is ~1-15ms, while four consecutive offset computations execute in nanoseconds), which would produce identical LCG seeds and therefore identical offsets for all four values — defeating "independently randomized" (FR-002/FR-003). Mixing in a monotonically incrementing counter guarantees each call produces a distinct seed even when the wall-clock tick hasn't advanced, while still avoiding `System.Security.Cryptography` (CA5394) since this is positional jitter, not a security control.

**Alternatives considered**:
- `System.Random` instance per call (`new Random()`) — pre-.NET 6, seeded from the clock with the same collision risk; also an extra allocation per coordinate.
- A single shared `System.Random` instance — `System.Random` is not thread-safe; would require a lock, adding contention on a hot path for a non-security feature.
- `Random.Shared` (.NET 6+, thread-safe) — simplest option, but triggers CA5394 ("Do not use insecure randomness") in this codebase's analyzer configuration unless suppressed; the project's established convention (per `SequenceRunner`) is the LCG fallback specifically to avoid that warning. Following the established pattern keeps the analyzer baseline clean without new suppressions.

## R-003: AppConfig Extension Pattern

**Decision**: Add one new property to `AppConfig` (`src/GameBot.Domain/Config/AppConfig.cs`): `TapJitterRadiusPx` (int, default 5), documented with an XML doc comment following the existing style (purpose, env var mapping, validation behavior), placed near `TapRetryCount`/`AdbRetries`.

**Rationale**: Matches the existing pattern exactly (`TapRetryCount`, `TapRetryProgression`, `AdbRetries`, `AdbRetryDelayMs` are all simple `{ get; set; }` properties with default values and XML docs referencing their `GAMEBOT_*` env var). No structural changes to `AppConfig` needed.

**Alternatives considered**:
- Separate `JitterConfig` class — adds another DI registration/concept for a single scalar; existing `AppConfig` is the established home for exactly this kind of tunable.

## R-004: Configuration Wiring (Validation, Snapshot, Env Var)

**Decision**: Wire `TapJitterRadiusPx` through all four existing configuration touchpoints, following the `TapRetryProgression`/`TapRetryCount` precedent at each:

1. **`Program.cs`**: parse `GAMEBOT_TAP_JITTER_RADIUS_PX` at startup with `int.TryParse(...) is var v && v >= 0 ? v : 5` (negative → default 5, matching FR-006); pass into `new AppConfig { ... TapJitterRadiusPx = jitterRadius }`.
2. **`IConfigApplier`/`ConfigApplier.Apply`**: `_appConfig.TapJitterRadiusPx = GetInt(snapshot, "GAMEBOT_TAP_JITTER_RADIUS_PX", 5) is var jitter && jitter >= 0 ? jitter : 5;` — applies the same fallback when configuration is updated at runtime via the snapshot/applier path (default → saved file → env var precedence already implemented generically by `ConfigSnapshotService`).
3. **`ConfigSnapshotService.BuildDefaultRelevantKeys()`**: add `["GAMEBOT_TAP_JITTER_RADIUS_PX"] = 5` to the dictionary that seeds the generic UI Configuration variables list (satisfies FR-009/FR-011 — no bespoke UI component needed, the existing `ConfigParameterList` in web-ui renders this dictionary generically with current value/default/source).
4. **`ENVIRONMENT.md`**: add a `GAMEBOT_TAP_JITTER_RADIUS_PX` entry documenting purpose, default (5), and that 0 disables jitter while negative values fall back to 5 (FR-010).

**Rationale**: This is the exact same four-touchpoint wiring used for `GAMEBOT_TAP_RETRY_COUNT`/`GAMEBOT_TAP_RETRY_PROGRESSION` in feature 037. Reusing the established precedence chain (defaults → `data/config/config.json` → environment variables) automatically satisfies FR-008 with no new storage code.

**Alternatives considered**: None — this is a mechanical replication of an established, working pattern; no other approach was considered necessary.

## R-005: Clamping and Disable Semantics

**Decision**: `CoordinateJitter.Apply(x, y, radiusPx)` (and an analogous overload/usage for swipe endpoints) returns `(x, y)` unchanged when `radiusPx <= 0` (FR-005: 0 disables; defensively also treats any non-positive value reaching this point as "disabled", though `AppConfig`/`ConfigApplier` validation already guarantees `radiusPx >= 0` per FR-006/FR-007). When `radiusPx > 0`, each axis offset is drawn independently and uniformly from `[-radiusPx, +radiusPx]` (FR-003, square jitter per spec Assumptions), then `Math.Max(0, value)` clamps the result to non-negative (FR-007/SC-005). No upper-bound (screen size) clamp is applied, per spec Assumptions.

**Rationale**: Directly implements FR-003/FR-005/FR-007 with the simplest possible arithmetic; matches the spec's explicit "square jitter, lower-bound-only clamp" assumption.

**Alternatives considered**:
- Circular (Euclidean) jitter using polar coordinates — explicitly rejected by the spec's Assumptions section in favor of simpler independent per-axis bounds.
- Clamping to a configured max screen size — rejected per spec Assumptions (screen dimensions not consistently available at the jitter point; default ±5px is negligible).

## R-006: PrimitiveTapStepOutcome Extension for Pre/Post-Jitter Reporting

**Decision**: Extend `PrimitiveTapStepOutcome` (`src/GameBot.Service/Services/ICommandExecutor.cs`) with three new optional, additive fields (default `null`, appended after existing parameters to preserve positional-constructor compatibility):

- `PrimitiveTapResolvedPoint? ExecutedPoint` — the post-jitter (x, y) actually sent to the device for a tap-shaped step (the existing `ResolvedPoint` continues to represent the pre-jitter target).
- `PrimitiveSwipePoints? TargetSwipe` — the pre-jitter (start, end) points for an explicit swipe step.
- `PrimitiveSwipePoints? ExecutedSwipe` — the post-jitter (start, end) points actually sent for an explicit swipe step.

A new record `PrimitiveSwipePoints(PrimitiveTapResolvedPoint Start, PrimitiveTapResolvedPoint End)` is added alongside the existing `PrimitiveTapResolvedPoint(int X, int Y)`.

**Rationale**: Implements the clarified FR-013/SC-006 decision ("Both — keep the existing target/resolved point field, and additionally record the actual jittered coordinates"). Adding new optional trailing parameters to a `sealed record` is backward compatible with all existing positional-constructor call sites (they continue to compile, defaulting the new fields to `null`). `CommandExecutor` populates `ExecutedPoint`/`ExecutedSwipe` by reading the (now-jittered) values back out of the `InputAction.Args` dictionary after `SendInputsAsync` returns (per R-001).

**Alternatives considered**:
- Encode jittered coordinates as a string suffix in the existing `Reason` field (as feature 037 did for retry counts) — rejected because the clarification explicitly calls for *coordinates* to be recorded structurally (consumed by `ExecutionLogService` and DTOs as points, not parsed from text), and downstream API DTOs (`ResolvedPointDto`) already model points as structured `{X, Y}` objects.
- Replace `ResolvedPoint` in place with the jittered value — rejected; FR-013 requires *both* pre- and post-jitter values to remain available, and `ResolvedPoint` is consumed by existing tests/DTOs as the "target" semantic.

## R-007: Downstream Consumers of New Outcome Fields

**Decision**: Propagate the new `ExecutedPoint`/`TargetSwipe`/`ExecutedSwipe` fields additively through:

- `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` (`LogCommandExecutionAsync`) — extend the "Tap executed at (X,Y)" detail construction to also include the executed (jittered) point when it differs from the resolved (target) point, and add equivalent detail text for swipe steps showing target vs. executed start/end points.
- `src/GameBot.Service/Models/Commands.cs` — add `ExecutedPoint`/`TargetSwipe`/`ExecutedSwipe`-equivalent optional properties to the relevant DTOs alongside the existing `ResolvedPointDto`.
- `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` (~line 178-182) and `src/GameBot.Service/Endpoints/StepsEndpoints.cs` (~line 170) — map the new outcome fields into the response DTOs/anonymous objects, mirroring the existing `ResolvedPoint` → `ResolvedPointDto` mapping.

**Rationale**: All additions are additive (new optional fields/properties); no existing field is renamed, removed, or repurposed, so existing consumers (UI, tests) are unaffected unless they choose to read the new fields.

**Alternatives considered**: None — this is a direct, minimal-churn propagation of the new outcome fields to the points where `ResolvedPoint` is currently surfaced.

## R-008: Test Strategy

**Decision**:
- `tests/unit/Domain/CoordinateJitterTests.cs` (new): radius=0 → passthrough; radius=N → result within `[target-N, target+N]` on each axis across many samples; near-zero target with radius>0 never produces a negative coordinate; consecutive calls produce varying offsets (seed-uniqueness check per R-002).
- `tests/unit/Config/AppConfigValidationTests.cs`: add `DefaultTapJitterRadiusPxIsFive`, `TapJitterRadiusPxCanBeSetToZero`, `TapJitterRadiusPxNegativeFallsBackToDefault` (mirroring existing `TapRetryCount`/`TapRetryProgression` tests).
- `tests/unit/Emulator/SessionManagerJitterTests.cs` (new): verify `SendInputsAsync` mutates `InputAction.Args` for both `"tap"` and `"swipe"` types within the configured radius, that radius=0 leaves args unchanged, and that resulting coordinates are never negative for near-zero targets.
- `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs` / `CommandExecutorSwipeTests.cs`: extend to assert `ExecutedPoint`/`TargetSwipe`/`ExecutedSwipe` are populated correctly when the (fake) `SessionManager` mutates `Args`, while existing assertions against unmutated `RecordingSessionManager` fakes (which don't apply jitter) continue to pass unchanged.

**Rationale**: Covers all FRs (FR-001 through FR-013) at the unit level, satisfies the constitution's ≥80% line / ≥70% branch coverage target for touched areas, and confirms the "existing tests keep passing" finding from prior research (jitter only applies in the real `SessionManager`, not in `RecordingSessionManager` fakes).

**Alternatives considered**:
- Integration-level test asserting actual ADB dispatch coordinates — would require a real or mocked `AdbClient`; existing `SessionManager` tests already mock at the `AdbClient` boundary, so a unit-level test against `SendInputsAsync` with a stubbed/no-op ADB path is sufficient and faster.
