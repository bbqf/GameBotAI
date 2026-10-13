# Implementation Plan: Emulator Monitoring

**Branch**: `141-emulator-monitoring` | **Date**: 2026-10-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/141-emulator-monitoring/spec.md`

## Summary

Add a "Monitoring" area to the web UI (desktop and phone layout). It lists all sessions with a state, shows a
live screenshot of the selected session (poll every 2 s, no overlap, pause when hidden), and restarts the whole
emulator (confirm, progress, result). The backend adds `GET /api/sessions/monitoring`, `POST` and `GET
/api/sessions/{id}/restart`. The snapshot route already exists and is reused. The restart uses `ldconsole reboot`
through the existing `IEmulatorControl`, finds the LDPlayer instance from the device serial, waits for the device
(`RestartPollIntervalMs`, limit 2 min), and always reconnects the session by restarting its capture loop on the same
session id (FR-015). Each user story is proven by automated tests alone (FR-023). One final manual step on a real
emulator checks the SC items that have no automated check (FR-025). The same pull request updates
`docs/architecture.md`, `specs/STATUS.md`, the spec `Status` line, and `CHANGELOG.md` (FR-026, FR-027, FR-028).

## Technical Context

**Language/Version**: C# on .NET (existing solution), TypeScript with React (existing web-ui)
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (`ISchemaFilter`), existing `ISessionManager`, `IEmulatorControl`, `IEmulatorDeviceProbe`, `ISessionLivenessService`, `BackgroundScreenCaptureService`; React, Jest, Vite
**Storage**: N/A (restart records are in memory)
**Testing**: xUnit unit and contract tests (`WebApplicationFactory`); Jest with fake timers for the web UI
**Target Platform**: Windows host with LDPlayer (`ldconsole`); browser (desktop and phone)
**Project Type**: Web service plus web UI
**Performance Goals**: First picture in 3 s; change visible in 5 s; one screenshot request at a time, and only for the selected session (SC-005)
**Constraints**: Restart ends in at most 2 min (`RestartTimeoutMs` 120000); poll between device checks `RestartPollIntervalMs` (default 3000, FR-022); fixed 2 s screenshot interval (FR-005)
**Scale/Scope**: One operator; a few sessions; 1 new endpoint file, 1 service, 1 mapper, 1 page, 1 hook, 2 to 3 components

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code quality | Pass | Thin endpoint file; logic in `SessionRestartService`, `LdInstanceMapper`; `LoggerMessage` logging; no giant methods (build analyzers). |
| II. Testing | Pass | Unit tests (mapper, restart service with fake control, failed-screenshot log entry FR-017), contract tests (all routes, route clash FR-021, only the selected session requested SC-005), Jest tests (hook timer FR-024, page, dialog, navigation). Every user story passes on automated tests alone (FR-023). |
| III. UX consistency | Pass | Phone support level `full`; follows `docs/mobile-ui-principles.md` (single column, no sideways scroll, 44 px targets, hover in `@media (hover: hover)`). |
| IV. Performance | Pass | Poll only the selected session; no overlap; pause when hidden; list state does not probe the device. |
| V. Living documentation | Pass | OpenAPI descriptions through `ISchemaFilter` (FR-018); update `docs/mobile-ui-principles.md`; document `Service:Monitoring` options; update `docs/architecture.md` with the new API routes, the monitoring capability, and the emulator restart capability, and set a new "Last reviewed" date (FR-026); add a `CHANGELOG.md` entry under `[Unreleased]` > `Added` (FR-028); set the spec `Status` line and add a `specs/STATUS.md` row (FR-027). |
| VI. STE | Pass | All new text in STE. |

Post-design re-check: pass. No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/141-emulator-monitoring/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── monitoring-api.md
│   └── monitoring-ui-contract.md
└── tasks.md             # Phase 2 output (/speckit.tasks - not created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── GameBot.Emulator/Adb/LdInstanceMapper.cs                    # serial -> LDPlayer index (Foundational, task T2)
├── GameBot.Service/
│   ├── Endpoints/SessionMonitoringEndpoints.cs                 # list, POST/GET restart (thin)
│   ├── Endpoints/Dto/SessionMonitoringDtos.cs
│   ├── Services/Monitoring/                                    # options, record, ISessionRestartService + impl,
│   │                                                           # ISessionReconnector + impl, logging
│   └── Swagger/                                                # ISchemaFilter for new DTOs
└── web-ui/src/
    ├── types/navigation.ts                                     # area `monitoring`, order 2
    ├── services/monitoringApi.ts
    ├── hooks/useSequentialPolling.ts
    ├── components/monitoring/                                  # ScreenshotView, SessionList, RestartEmulatorDialog
    ├── pages/MonitoringPage.tsx, Monitoring.phone.css
    └── App.tsx                                                 # wire the area
tests/
├── unit/        LdInstanceMapperTests, SessionRestartServiceTests (includes failed-screenshot log test)
└── contract/    One file for each user story, for example
                 SessionMonitoringListContractTests (US1, includes route clash test),
                 SessionMonitoringRestartContractTests (US2)
src/web-ui tests: hooks/__tests__/useSequentialPolling.test.tsx, pages/__tests__/MonitoringPage*.test.tsx,
  components/monitoring/__tests__/, and the existing navigation tests (see below)
docs/mobile-ui-principles.md
docs/architecture.md                                            # routes, monitoring, restart, Last reviewed (FR-026)
specs/STATUS.md                                                 # new row for 141 (FR-027)
specs/141-emulator-monitoring/spec.md                           # final Status line (FR-027)
CHANGELOG.md                                                    # [Unreleased] > Added (FR-028)
```

**Structure Decision**: Web application layout already in the repository: backend in `src/GameBot.Service` and `src/GameBot.Emulator`, frontend in `src/web-ui`. No new project.

### Existing navigation tests to update (area order and count change)

- `src/web-ui/src/components/__tests__/Navigation.test.tsx`
- `src/web-ui/src/types/__tests__/navigation.test.ts`
- `src/web-ui/src/__tests__/navigation.spec.tsx`

Adding the area at order 2 moves later areas up by one. These three files can assert order, count, or labels. Update them
and add Monitoring checks (desktop navigation, phone level `full`). Tasks that edit the same file are not parallel.

## Design decisions tied to the corrected spec

- **FR-021 route clash**: `GET /api/sessions/monitoring` and `GET /api/sessions/{id}` are in the same route group. A literal segment wins over `{id}` in ASP.NET Core routing, but the plan does not rely on that. Contract tests prove: (a) `GET /monitoring` returns 200 with the list, never `not_found`; (b) a real session id still reaches `GET /api/sessions/{id}`, `/snapshot`, `/health`; (c) a session whose id is the text `monitoring` is not reachable by the list route (the list wins; no such id is generated by the service).
- **FR-015 reconnect (always)**: after the device answers, `SessionRestartService` calls `ISessionReconnector.ReconnectAsync(sessionId, serial)`. The real implementation stops the capture loop (`StopCapture`) and starts it again (`StartCapture`) for the same session id. The session id does not change, so the UI selection and the queue owner link stay valid. A fake reconnector in tests proves the call (FR-023). No manual rebind step.
- **FR-022**: `SessionMonitoringOptions.RestartPollIntervalMs` (default 3000) is the wait between device checks. A unit test with a fake clock sets it small.
- **FR-024**: Jest fake-timer test for `useSequentialPolling`: next request at 2 s after the start of the last one, and none while one is open.
- **FR-025**: Manual steps (SC-001, SC-002, SC-004, SC-006) are one final task, separate from the stories.
- **FR-002 and FR-012 all sessions**: the list comes from `ISessionManager.ListSessions()` with no status filter; states: `live`, `no_answer`, `restarting`, `stopping`. The list and the snapshot route cover FR-012; the existing snapshot route is reused.
- **FR-017 logs**: `MonitoringLog` writes restart steps (request, refusal, start, success, failure) and each failed screenshot request, with session id and result. A successful screenshot request is not logged. A test with a capturing logger proves that a failed screenshot writes one entry and a successful one writes none. The log test opens the logging gate for its component first.
- **SC-005 selected session only**: the screenshot route is called for the selected session only. A Jest test proves that the page requests no snapshot for other sessions. The hook allows one open request at a time.
- **Contract test files (Assumptions)**: one contract test file for each user story. No two parallel tasks edit the same file.
- **`LdInstanceMapper` placement**: it is in the Foundational phase (task T2), not in one user story, because the list (restart availability) and the restart both use it.
- **FR-026 docs**: `docs/architecture.md` gets the three routes, the monitoring capability, the emulator restart capability, and a new "Last reviewed" date.
- **FR-027 status**: at the end, set the `Status` line in `spec.md` and add a row `141 | Emulator Monitoring | <status word>` to `specs/STATUS.md`. Use the status words of that file (spec 140 is the example).
- **FR-028 changelog**: one entry under `[Unreleased]` > `Added`. It names the Monitoring area, the three routes, the setting `RestartPollIntervalMs`, and `141-emulator-monitoring`.

## Complexity Tracking

No violations. Table not needed.
