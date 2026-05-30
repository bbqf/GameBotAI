# Research Log – Runtime Logging Control

## Decision: Persist overrides in existing JSON config repository
- **Rationale**: The service already maintains JSON configuration under `data/config`, so extending that store keeps deployment/simple file sync flows intact, respects backup policies, and avoids introducing new infrastructure. Serializing component-level overrides (component name, enabled flag, level, lastChanged metadata) fits the existing schema patterns used for triggers/actions.
- **Alternatives considered**:
  - *Dedicated database table*: Adds operational overhead for a small dataset (<20 components) and would require new provisioning.
  - *In-memory only*: Fails persistence and restart-resiliency requirements.

## Decision: Use `/config/logging` REST surface with GET/PUT semantics
- **Rationale**: Reusing the config namespace minimizes discoverability cost and inherits existing authentication/authorization filters. A GET returning all components and a PUT/PATCH accepting component deltas matches the user stories (view state, set individual levels, bulk reset) and leverages ASP.NET Core model binding.
- **Alternatives considered**:
  - *Separate controller/root*: Adds routing churn with no security benefit.
  - *Multiple bespoke endpoints per action*: Increases maintenance and client complexity; batch payloads already cover per-component adjustments and resets.

## Decision: Immediate propagation via `ILoggerFactory` level switch per component
- **Rationale**: ASP.NET Core logging supports `LoggerFilterOptions` updated at runtime by injecting `LoggerFilterOptionsMonitor` or custom `LoggingRuleUpdater`. Mapping each logical component to a `LoggerFilterRule` ensures that when REST updates occur we can update the in-memory filter dictionary and have the change take effect on the next log statement (sub-second). This satisfies the 5-second SLA without restarting.
- **Alternatives considered**:
  - *Rebuilding the host / restarting app*: Violates requirement for zero restarts.
  - *Delayed background refresh*: Introduces lag and complicates reasoning when multiple changes occur quickly.

## Decision: Last-write-wins with optimistic concurrency guards
- **Rationale**: Administrative operations are rare and low-volume. Using timestamps + actor metadata in the persisted document lets us detect stale updates (if needed later) while keeping API simple. For now, last write wins per requirements (“Simultaneous updates resolve deterministically”). Logging audit entries provide recovery options if a change was unintended.
- **Alternatives considered**:
  - *Versioned etags/If-Match headers*: Adds protocol complexity that is unnecessary at current scale.
  - *Queue-based change sequencer*: Overkill for single-instance admin actions; rehydration already fast.

## Decision: Authorization piggybacks existing config scope
- **Rationale**: GameBot.Service already gates `/config` endpoints behind the `GAMEBOT_AUTH_TOKEN` or configured roles. Extending the same policy means no new secrets or roles are required, and it keeps the security posture aligned with other mutable configuration entry points.
- **Alternatives considered**:
  - *New scope/claim*: Adds user-management burden without proven need.
  - *Unauthenticated internal endpoint*: Violates security principle and constitution testing standards.
