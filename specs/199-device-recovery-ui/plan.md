# Implementation Plan: Device Recovery Option in the Queue Form

**Branch**: `199-device-recovery-ui` | **Date**: 2026-11-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/199-device-recovery-ui/spec.md`

## Summary

Feature 121 added the queue field `deviceRecovery`, but the web UI has no control for it. Also, each save from the queue form clears the field, because the form does not send it and the update endpoint clears an absent member.

The work:

1. **Service, update rule** (FR-005a): `UpdateQueueRequest` records whether the JSON has a `deviceRecovery` member. The update endpoint keeps the stored value when the member is absent. Only an explicit `null` clears it (research R-001).
2. **Service, running queue** (FR-012a): `QueueLivenessWatch` reads the stored queue at the start of each check and uses its current `deviceRecovery` and instance name. The episode and its attempt count do not change (R-002).
3. **Service, mark data** (FR-011a): the queue list gets `deviceLiveness` and the monitoring session list gets `ownerQueueDeviceLiveness`, both from the episode snapshot of the run (R-004).
4. **Web UI, form** (FR-001 to FR-009): the option and its three fields in `QueueForm`, the form checks, and the save rule of R-006. The running queue note changes (R-003).
5. **Web UI, state** (FR-011, FR-011a): a `QueueDeviceState` line in the running queue controls, and a "Not live" mark on the Monitoring session row and header, the phone queue card, and the phone queue detail.
6. **Docs** (FR-013): OpenAPI text, `docs/architecture.md`, `docs/mobile-ui-principles.md`, `CHANGELOG.md`, `specs/STATUS.md`, and the Status line of spec 121.

## Technical Context

**Language/Version**: C# (`LangVersion` preview) / .NET 9 (service); TypeScript 5 / React 18 (web UI, Vite)
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (schema filters), System.Text.Json; React, Jest + Testing Library (web UI)
**Storage**: One JSON file for each queue in `<data root>/queues/` (no format change)
**Testing**: xUnit + FluentAssertions (`tests/unit`, `tests/contract`, `tests/integration`); Jest (`src/web-ui`)
**Target Platform**: Windows service host; desktop and phone browsers
**Project Type**: web service with a web UI
**Performance Goals**: The watch adds one queue file read each check interval (1 minute) for each running queue: less than 5 ms each, 4 queues in production. The list reads add one snapshot copy (a lock and 8 values) for each running queue. No new web UI request: the form uses the detail that it reads now, the device state uses the 2.5 s health poll that it reads now, and the marks use the 5 s list polls that they read now.
**Constraints**: No change of the stored queue format, of `QueueLivenessEpisode`, of the recovery coordinator, or of the validation texts of feature 121. `POST /api/queues` keeps its rule. The new response members are additive.
**Scale/Scope**: 4 production queues. 1 form, 1 new small component, 3 views with a mark.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: Phone support level: the recovery option is `desktop-only`, as the rest of the queue settings form. The Queues area stays `reduced`. The "Not live" mark is `full` on the phone queue card, the phone queue detail, and the Monitoring session row and header (Monitoring is `full`). The mark follows `docs/mobile-ui-principles.md`: it wraps, it needs no tap, and it causes no horizontal scroll at 360 px.

| Gate | Status | Note |
|---|---|---|
| I. Code quality | PASS | Small changes in current classes. One new web UI component. Doc comments on the new members. Method names in CamelCase. No new dependency. |
| II. Testing | PASS | Bug fix (FR-005): a contract test that fails before the fix (a `PUT` with no `deviceRecovery` clears the stored value). Unit tests for the watch with changed settings (turn on in an old episode, turn off, lower `maxAttempts`). Contract tests for the two new list members. Jest tests for the form (show, defaults, load, save table of R-006, field checks, 400 text, note), for `QueueDeviceState`, and for the three marks. |
| III. UX consistency | PASS | The option follows the smart scheduling pattern (checkbox, help text, fields when on, message at the field). The phone levels are stated above. The texts are in the contract. |
| IV. Performance | PASS | Declared in Technical Context. The watch read is not on a hot path (once a minute). |
| V. Living documentation | PASS | Update `docs/architecture.md` (the update rule, the running queue rule, the two list members, "Last reviewed"), `docs/mobile-ui-principles.md` (the mark and the desktop-only option), `CHANGELOG.md`, `specs/STATUS.md` (row 199), and the Status line of spec 121: "Implemented (iterated by 199)". |
| VI. STE | PASS | All artifacts are in STE. The UI texts in the contract are in STE. |
| Version bump | PASS (later) | Bump `patch` in `installer/versioning/version.override.json` before the push (task T035). |

Post-design re-check: PASS. The design adds no project, no stored field, and no endpoint. It changes one API rule (update of `deviceRecovery`), which the clarification Q1 approved. The rule is additive for a client that sends the member.

## Project Structure

### Documentation (this feature)

```text
specs/199-device-recovery-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── queue-recovery-ui-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Contracts/Queues/UpdateQueueRequest.cs          # DeviceRecovery setter + DeviceRecoverySpecified
├── Contracts/Queues/QueueResponse.cs               # + DeviceLiveness (list only)
├── Endpoints/QueuesEndpoints.cs                    # update keeps an absent member; list sets DeviceLiveness
├── Endpoints/SessionMonitoringEndpoints.cs         # + OwnerQueueDeviceLiveness
├── Endpoints/Dto/SessionMonitoringDtos.cs          # + OwnerQueueDeviceLiveness
├── Services/QueueExecution/QueueLivenessWatch.cs   # read the stored queue at each check
├── Services/QueueExecution/QueueDeviceRecoveryRunner.cs  # MaxAttempts from the current settings
├── Services/QueueExecution/QueueExecutionService.cs      # give the read delegate to the watch
└── Swagger/QueueDeviceRecoverySchemaFilter.cs      # update rule and running queue text

src/web-ui/src/
├── services/queues.ts                              # QueueDeviceRecoveryDto, QueueDeviceLivenessDto, new members
├── services/monitoringApi.ts                       # + ownerQueueDeviceLiveness
├── components/queues/QueueForm.tsx                 # option, fields, checks, note
├── components/queues/deviceRecoveryForm.ts         # NEW: minutes <-> ms, checks, the save rule (pure functions)
├── components/queues/QueueDeviceState.tsx          # NEW: the device state line
├── components/queues/NotLiveBadge.tsx              # NEW: the "Not live" mark
├── components/queues/RunningQueueControls.tsx      # shows QueueDeviceState
├── components/queues/PhoneQueuesView.tsx           # mark on the card and in the detail
├── components/monitoring/SessionList.tsx           # mark on each session row of the Monitoring list
├── pages/MonitoringPage.tsx                        # mark in the header of the selected session
└── pages/QueuesPage.tsx                            # form value load and save

tests/unit/Queues/              # watch with changed settings; request flag
tests/contract/Queues/          # PUT absent vs null; list member; OpenAPI text
tests/contract/                 # monitoring member
src/web-ui/src/**/__tests__/    # form, state line, marks

docs/architecture.md, docs/mobile-ui-principles.md, CHANGELOG.md, specs/STATUS.md,
specs/121-device-not-live-alert-recovery/spec.md (Status line)
```

**Structure Decision**: The work stays in the current service and web UI projects. The form rules (unit change, checks, what to send) go into one pure module, `deviceRecoveryForm.ts`, so that Jest tests them without a render and `QueueForm.tsx` and `QueuesPage.tsx` stay small. The watch gets a read delegate, not the repository, so that its unit tests give a fake read.

## Complexity Tracking

No constitution violations.
