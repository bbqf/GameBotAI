# Research — Logging Config Refresh (004)

## Runtime Logging Performance Targets
- **Decision**: Guarantee that applying a new logging configuration (updating global/per-category level switches + persisting JSON) completes within 200 ms and that steady-state logging handles 2 000 events/sec with <5% extra CPU over baseline.
- **Rationale**: Operators primarily toggle verbosity while debugging spikes; 200 ms can be achieved because the service only rebuilds Serilog once per request and reuses sinks. 2 000 events/sec exceeds current observed traffic (≈600/sec) and gives >3× headroom.
- **Alternatives Considered**: Leaving the target unspecified would violate the constitution; aggressive 10 000 events/sec goal was rejected because I/O bandwidth (console/ETW) becomes the bottleneck and would require buffering infrastructure outside project scope.

## Serilog + Microsoft.Extensions.Logging Synchronization
- **Decision**: Use a singleton `SerilogLevelSwitchRegistry` plus a `LoggingLevelSwitch` per category, and rebuild the Serilog logger via `LoggerConfiguration.MinimumLevel.ControlledBy(globalSwitch).MinimumLevel.Override(category, switch)` while injecting the same overrides into `LoggerFilterOptions.Rules` for Microsoft logging.
- **Rationale**: Level switches allow atomic runtime adjustments without recreating every `ILogger<T>`. Keeping Microsoft filter rules in sync prevents providers (e.g., `ConsoleLoggerProvider`) from emitting unsuppressed messages, satisfying UX requirements for deterministic log output.
- **Alternatives Considered**: Relying solely on `ILoggerFactory.AddFilter` per category failed because different providers evaluate rules at creation time; dynamic filter updates are not guaranteed to propagate and would miss the Serilog console sink.

## Authenticated ASP.NET Core Minimal API Endpoint Pattern
- **Decision**: Expose `POST /api/logging/config` (and optional `GET`) under token auth enforced by existing middleware, require `Authorization: Bearer <token>` header, and validate payloads via FluentValidation or manual model validation before persisting.
- **Rationale**: Reusing the token middleware keeps behavior consistent with other protected maintenance endpoints and satisfies the constitution’s UX/security clauses (actionable errors, no secret leakage). Minimal APIs simplify parameter binding and ensure low latency for config refresh.
- **Alternatives Considered**: Exposing anonymous endpoints would violate security requirements; introducing OAuth was deemed overkill for single-operator emulator hosts.

## File-Based JSON Persistence Pattern
- **Decision**: Persist the effective logging configuration under `data/config/config.json` alongside existing service config, version fields, and `LastUpdatedUtc`. Use `IOptionsMonitor` or a simple repository wrapper to reload defaults at startup, then track runtime overrides separately so they can be merged/diffed.
- **Rationale**: Operators already manage JSON config files for the emulator; keeping a single file avoids additional storage dependencies. Including metadata enables audit trails when multiple operators adjust verbosity.
- **Alternatives Considered**: Storing overrides in environment variables was dismissed because it requires restarts; using SQLite would add an unnecessary dependency for a tiny dataset (<50 KB).

## Test Strategy for Dynamic Logging
- **Decision**: Capture Serilog output via a `StringWriter` sink chained into the runtime logger, and use `WebApplicationFactory<Program>` with deterministic auth tokens to exercise the `/api/logging/config` endpoint plus direct service invocation for faster unit tests.
- **Rationale**: This approach keeps tests deterministic (no race to attach sinks) and exercises both HTTP and service layers, satisfying the constitution’s testing clause for determinism and coverage. Chaining the sink ensures log output persists after each reconfiguration.
- **Alternatives Considered**: Mocking `ILogger` abstractions would not validate the actual output template, while hitting external log aggregation services would make tests flaky and slow.
