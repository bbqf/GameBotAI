# Research: Notify Step Delivers to the Notification Targets

## Findings about the current code

- `SequenceExecutionService.DispatchNotifyAsync` calls only `IFailureNotifier.NotifyAsync(evt, payload.Url)`.
- `HttpFailureNotifier.DeliverAsync` uses `url`, else `Service:Notifications:DefaultUrl`. With neither, it logs and returns `FailureNotificationResult(false, ..., "no destination configured")`. The step then says "notification not delivered: no destination configured" and stays `success`.
- The notification targets (for example `telegram`) are read by `QueueNotificationWorker` through `INotificationTargetStore.List()`. The worker sends through `INotificationChannel` objects with `ChainSend` and `SendOneAsync`. Queue alerts (feature 121) already send to all enabled targets this way.
- The step code does not know the worker or the targets.

## R1: Where does the target send run?

- **Decision**: In the existing `QueueNotificationWorker`, as a new work item "text send" on the existing dispatcher channel.
- **Rationale**: The worker owns the target list handling, the per-target send order (`ChainSend`), the time limit, the unknown-type handling, and the safe logs. Reuse gives the same behaviour as queue alerts and avoids a second copy of the send code. The user request for this feature is to reuse the dispatcher and the target delivery.
- **Alternatives considered**: (a) A new direct sender class that calls the channels in the step. It copies the send and log code, and has no send order. (b) Make `HttpFailureNotifier` send to targets. It is the wrong layer: the notifier is the URL path and the queue failure policy also uses it, which would change queue behaviour (FR-009).

## R2: How does the step learn the result?

- **Decision**: The work item carries a `TaskCompletionSource<TextSendOutcome>`. The worker completes it. The step awaits it with the caller token and a guard time limit (`NotificationDispatchLimits.SendTimeout`, 30 s, plus the fixed constant `TextSendGuardMargin`, 5 s: 35 s in total). A guard time-out is treated as `Failed("time limit")`.
- **Rationale**: The step already awaits the notifier and needs the result for `RecordNotification`, dedupe commit or release (feature 137), and its message. This matches the `QueueAlert.OnCompleted` idea but gives an awaitable result.
- **Alternatives considered**: Fire and forget (no result): it breaks dedupe commit, because the slot could not tell a failed send from a good one. An `Action` callback like `QueueAlert`: it needs extra glue to await.

## R3: What if no enabled target exists?

- **Decision**: The outcome is `NoTarget`. The step then runs the current notifier path with `url` empty. This keeps `DefaultUrl` working and keeps the current log and step text (FR-006, FR-004 only applies when a target exists).
- **Rationale**: A site that uses `DefaultUrl` and has no target keeps its behaviour. A site with a target now gets the message on the target.
- **Alternatives considered**: Send to targets and `DefaultUrl` together. The spec says the default destination is the targets, and an operator could get two copies. Rejected.

## R4: Send cap and queue of jobs

- **Decision**: The text send is not counted in `MaxQueuedJobs` and is not dropped by `MaxParallelSends`, as for alerts. It is a rare, author-written message. The dispatcher call never throws. If the channel write fails, the outcome is `Failed("not queued")`.
- **Rationale**: The author wrote the step to send. A silent drop is the defect of this issue. The number of steps is small and each send has a time limit.
- **Alternatives considered**: Count it in the cap. A burst of queue jobs could then drop an author message.

## R5: Message text and the "last sent" state

- **Decision**: The text is the step `message` with no prefix (spec clarification, FR-011). The worker does not call `_lastSent.Record` for this send.
- **Rationale**: Feature 126 drops a repeated failure when it equals the last message sent to the target. An author message must not hide or replace that state.
- **Alternatives considered**: Record it. A later identical queue failure message could be wrongly kept or dropped.

## R6: Send order key

- **Decision**: `ChainSend(target, text, scopeKey, "notify:" + sequenceId)`. `scopeKey` is the originating queue id, or `adhoc` when there is none.
- **Rationale**: This keeps the order of messages from one sequence and queue to one target, and does not block queue messages (different key).

## R7: One message for each target for each step run

- **Decision**: The worker makes exactly one `ChainSend` call for each enabled target. There is no retry in the worker. (The Telegram channel keeps its own two attempts inside one send, which is one message.)
- **Rationale**: FR-011 and SC-002.

## R8: Step outcome

- **Decision**: Unchanged: `success` (or `sent` with a dedupe reservation, `suppressed` when the window has an alert). `Delivered` gives the step message "notification delivered". `Failed` gives "notification not delivered: <safe reason>". A target fault only appears in the worker log.
- **Rationale**: FR-012 and feature 087 (the guard sequence MUST finish its own logic).

## R9: Step-through and validation

- **Decision**: No code change. `StepThroughActionPreview` handles `notify` in its own path (`DescribeNotify`) and never calls `DispatchNotifyAsync`, so it never uses the target path. A unit test MUST prove this: with an enabled target, a step-through of a `notify` step calls no `INotificationDispatcher.SendText` and no `IFailureNotifier` (FR-014). The `notify` save-time validation in `SequenceStepValidationService` does not change.
- **Rationale**: Spec edge cases and FR-014.

## R10: OpenAPI text

- **Decision**: Edit the `[ActionTypes.Notify]` text in `PrimitiveActionSchemaFilter`. The `url` part says: when `url` is not set, the message goes to the enabled notification targets (see `GET /api/notifications/targets`, managed with the notification target routes); when `url` is set, the message goes only to that URL. Remove "the service's default destination" as the only default and say that `Service:Notifications:DefaultUrl` is used only when no target is enabled.
- **Rationale**: FR-008, user story 3.

## R11: Living documentation and version

- **Decision**: Update `docs/architecture.md` (Notify action paragraph near line 323, notify dedupe paragraph near line 434, "Last reviewed" date), `CHANGELOG.md`, `specs/STATUS.md`. Bump `installer/versioning/version.override.json` before the push (CLAUDE.md rule).

## R12: Test-first order (FR-013)

- **Decision**: The first test is a regression test in `tests/unit/Sequences/NotifyStepTargetDeliveryTests.cs`: no `url`, one enabled target, the message text reaches the target. It uses a fake `INotificationChannel` and the real dispatcher and worker, so it compiles on the current code. It MUST run red on the current code (the target gets nothing, and the log says "no destination configured"). Record the red result in the task notes or commit text. Then do the fix.
- **Rationale**: FR-013 and Constitution Principle II (a bug fix has a failing test first).
- **Alternatives considered**: A test that needs the new `SendText` member. It would fail to compile, not fail on behaviour, so it is not proof of the defect.

## R13: Spec Status and STATUS.md (FR-015)

- **Decision**: In the last task, set `**Status**:` in spec.md to `Implemented` and update the row for 159 in `specs\STATUS.md`. Check the Status line of spec 137 (iterated by 159) and update `specs\STATUS.md` for it in the same way.
- **Rationale**: FR-015 and Constitution Principle V.

## R14: Partial success (FR-017)

- **Decision**: The worker completes the outcome as `Delivered` when at least one target accepts. When some fail, the worker log names how many failed (count only, no secret). A test covers one good target and one failing target.
- **Rationale**: The operator got the message on at least one target.

## R15: Spec 137 status (FR-018)

- **Decision**: In the last task, set the Status of spec 137 to "Implemented (iterated by 159)" and update its row in `specs\STATUS.md`. This is not conditional.

## R16: Dry-run test base (FR-019)

- **Decision**: Before the dry-run test, the implementer reads `StepThroughActionPreview` (`DescribeNotify`, outcome `SequenceStepper.PreviewOutcome`) and confirms the path and the outcome name. The test uses the confirmed names.

## R17: Guard time limit value (FR-016)

- **Decision**: Guard = `NotificationDispatchLimits.SendTimeout` (default 30 s) + `TextSendGuardMargin` (new constant, 5 s). The worker limit covers all targets because sends run in parallel.
- **Alternatives considered**: A new setting. Rejected: no need for a new option.

All items resolved. No NEEDS CLARIFICATION remains.
