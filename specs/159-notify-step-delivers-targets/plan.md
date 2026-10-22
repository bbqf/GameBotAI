# Implementation Plan: Notify step delivers to the notification targets

**Branch**: `159-notify-step-delivers-targets` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/159-notify-step-delivers-targets/spec.md`. Closes issue #335.

## Summary

A `notify` step with a `message` and no `url` now sends the message to every enabled notification target. Today the step only calls `IFailureNotifier`, which has no knowledge of the targets, so the log says "notification not delivered: no destination configured".

Approach (see research.md):

- Add a "text send" work item to the existing `QueueNotificationDispatcher` and `QueueNotificationWorker`. The worker already owns the target list, the per-target send order, the time limit, and safe logs.
- The work item carries a `TaskCompletionSource`. The step awaits the result and keeps its dedupe, record, and step-message logic.
- When no enabled target exists, the step uses the old notifier path (`DefaultUrl`, else the log). A `url` still overrides: the target path is not used.
- The work starts with a regression test that fails on the current code (FR-013). A step-through test proves that a dry run sends nothing (FR-014). At the end the spec Status and `specs\STATUS.md` are updated (FR-015).
- The OpenAPI text of `notify` names the enabled targets as the default destination (FR-008).

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core, Swashbuckle (OpenAPI schema filter), existing `INotificationChannel` objects (Telegram and others)
**Storage**: N/A. No stored data changes. Targets are read through `INotificationTargetStore.List()`.
**Testing**: xUnit in `tests/unit`, `tests/contract`, `tests/integration`
**Target Platform**: Windows service host (GameBot.Service)
**Project Type**: Web service with a web UI (no UI change)
**Performance Goals**: No new load. One send per enabled target per step run.
**Constraints**: One message per target per step run (FR-011). Queue notification behaviour and format unchanged (FR-009). Step outcome stays `success` on a fault (FR-012). No secret in logs.
**Scale/Scope**: About 5 source files, 4 new test files, documentation files.

No item is marked NEEDS CLARIFICATION.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- Build and test baseline: T001 records a green base before the change. Implementation does not start on a red base.
- Principle I (Code Quality): small, internal types. The new code reuses the worker send code. No new target type (FR-010). Pass.
- Principle II (Testing): unit, integration, and contract tests are planned. "Bug fixes MUST include a failing test reproducing the issue before the fix": FR-013 and the first tasks follow this rule. Coverage of the touched code at or above 80% line and 70% branch. Pass.
- Principle III (UX): No web UI change. The phone check does not apply. Only the OpenAPI text changes.
- Principle IV (Performance): No new hot path. The text send is a rare author message and has a time limit. Pass.
- Principle V (Living Documentation): `docs/architecture.md` and its "Last reviewed" date are updated. The spec Status and `specs\STATUS.md` are updated (FR-015). The Status of spec 137 is set to "Implemented (iterated by 159)" and its `specs\STATUS.md` row is updated (FR-018). Pass.
- Principle VI (STE): all text in the artifacts and the code comments follows ASD-STE100. Pass.
- Version bump: `installer/versioning/version.override.json` is bumped before the push (CLAUDE.md rule).

**Post-design re-check**: No violation. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/159-notify-step-delivers-targets/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── notify-step.md   # Phase 1 output
├── checklists/
│   ├── requirements.md
│   └── red-evidence.md      # Failing and passing test proof for FR-013 (made during implementation)
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Services/Notifications/
│   ├── NotificationTypes.cs              # TextSend, TextSendOutcome, NotificationWork.Text, SendText member
│   ├── QueueNotificationDispatcher.cs    # SendText
│   └── QueueNotificationWorker.cs        # HandleTextAsync
├── Services/SequenceExecution/
│   └── SequenceExecutionService.cs       # DispatchNotifyAsync uses the target path
├── Services/StepThrough/
│   └── StepThroughActionPreview.cs       # No change; a test proves no send (FR-014)
└── Swagger/PrimitiveActionSchemaFilter.cs # OpenAPI text of notify

tests/
├── unit/Notifications/QueueNotificationWorkerTextSendTests.cs
├── unit/Sequences/NotifyStepTargetDeliveryTests.cs       # includes the first (regression) test and the dry-run test
├── contract/Sequences/NotifyActionOpenApiTextTests.cs
└── integration/Notifications/NotifyStepToTargetsTests.cs

docs/architecture.md, CHANGELOG.md, specs/STATUS.md, specs/159-.../spec.md (Status)
installer/versioning/version.override.json
```

**Structure Decision**: Single service project (`GameBot.Service`) with the existing test projects. No new project.

## Design Decisions (summary of research.md)

| Topic | Decision |
|-------|----------|
| Where the send runs (R1) | In `QueueNotificationWorker`, as a new `Text` work item |
| Result to the step (R2) | `TaskCompletionSource<TextSendOutcome>` with a guard time limit: `NotificationDispatchLimits.SendTimeout` (30 s) plus the fixed margin `TextSendGuardMargin` (5 s) = 35 s (FR-016) |
| Partial success (R14) | "Delivered" when at least one target accepts; the log names the failed count (FR-017) |
| Spec 137 status (R15) | Set to "Implemented (iterated by 159)" and update its row in `specs\STATUS.md`, not conditional (FR-018) |
| Dry-run test base (R16) | Implementer confirms the `StepThroughActionPreview` path (`DescribeNotify`) and its outcome name (`preview`) before the test (FR-019) |
| No enabled target (R3) | `NoTarget`: the old notifier path with empty `url` |
| Send cap (R4) | Not counted in `MaxQueuedJobs` |
| Text and last-sent state (R5) | Plain `message`; `_lastSent` is not changed |
| Send order key (R6) | `notify:<sequenceId>` with scope = queue id or `adhoc` |
| One message per target (R7) | One `ChainSend` per enabled target, no worker retry |
| Step outcome (R8) | Unchanged: `success` |
| Step-through (R9) | Separate preview path; no send; a test covers it (FR-014) |
| Test order (R12) | Regression test first, shown red, then the fix (FR-013) |
| Status and docs (R13) | Spec Status and `specs\STATUS.md` set at the end (FR-015) |

## Implementation Order (for /speckit-tasks)

1. Record the green base.
2. Write the regression test (no `url`, one enabled target, the message reaches the target). Run it. It MUST fail on the current code. Keep the red result as evidence (FR-013).
3. Add the types, dispatcher member, and worker handler.
4. Change `DispatchNotifyAsync`. The regression test turns green.
5. Add the other unit, integration, `url` override, time-limit (FR-016), partial-failure (FR-017), and dry-run tests (FR-014). Before the dry-run test, confirm the step-through path and outcome name in `StepThroughActionPreview` (FR-019).
6. Change the OpenAPI text and its contract test.
7. Update documentation, set the spec Status to Implemented, set spec 137 to "Implemented (iterated by 159)" (FR-018), update `specs\STATUS.md` for both (FR-015), bump the version.

## Guard Time Limit (FR-016)

The step waits at most `NotificationDispatchLimits.SendTimeout` (default 30 s, the existing worker send time limit for all targets together) plus the new fixed constant `QueueNotificationDispatcher.TextSendGuardMargin` (5 s). Total: 35 s by default. The worker sends to all targets in parallel (`Task.WhenAll`), so the worker limit covers all targets. On expiry, the outcome is `Failed("time limit")`, the step stays `success`, and a test shows this with a channel that does not answer.

## Complexity Tracking

No violation. The table is not needed.
