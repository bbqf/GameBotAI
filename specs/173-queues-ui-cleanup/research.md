# Research: Clean up the Queues page on the big screen (173)

All items below resolve the open points of the spec. No NEEDS CLARIFICATION remains.

## R1: Activity text for many queues (FR-008, FR-020, FR-021)

- **Decision**: Move the activity rule to one shared function in `queueActivity.ts`. The function takes the queue status, the monitor snapshot, and the pause state. It returns the text. The big screen list and the phone card both call it.
- **Rationale**: FR-020 needs the same text, character for character, from one rule. `buildCardSummary` already holds the rule for the phone. It needs one new input (the pause state).
- **Data source**: The list calls `GET /api/queues/{id}/monitor` for each running queue, as the phone list does. It also reads the pause state. `GET /api/queues/{id}` has the health block with `operatorPause`. The plan adds the pause state to the list data (R2), so the list makes no extra call for it.
- **Refresh**: The big screen list polls every 5 s with `usePolling`, as the phone list does, and stops when the page is hidden.
- **Alternatives considered**: (a) Copy the rule into the page: rejected, the two copies would drift.
- **Alternatives considered**: (b) A new batch endpoint for all monitors: rejected for now. One running queue for each emulator keeps the count small (about 1 to 10 queues).

## R2: Pause state in the queue list (FR-021)

- **Decision**: Add an optional field `operatorPause` (`none`, `pausing`, `paused`) to the list item of `GET /api/queues`. The field is absent for a stopped queue.
- **Rationale**: Without it, the list needs one detail call for each running queue. The service already knows the state (feature 171). It holds the state in the run handle. So the list route gets the run registry (`IQueueRunRegistry`) and reads the handle with `TryGetLiveRun`. The change is additive, so old clients keep working.
- **Alternatives considered**: One detail call for each running queue: rejected, it doubles the requests of each poll.

## R3: Settings of a running queue (FR-019)

- **Decision**: Allow `PUT /api/queues/{id}` for a running queue. The service ignores a changed emulator (or refuses it with 400) and saves the other settings. A running run holds its own copy of the settings, so each saved setting takes effect at the next start. The notification level keeps its own call, and it applies to the next finished sequence (feature 120).
- **Rationale**: The run reads `queue.CycleExecution`, `PauseWhenIdle` and `IdleThresholdSeconds` from the loaded queue object. The run does not load the queue again. The simplest true statement for the operator is "applies at the next start". The Edit page shows this text near the Save button.
- **Open check for tasks**: A test MUST prove that a saved setting does not change a running run. If a setting does change the run, the text for that setting changes to "applies at once".
- **Alternatives considered**: Keep the 409 and make the settings read-only: rejected, the operator chose to keep them editable (Clarifications).

## R4: Editor on a running queue (FR-010, FR-011, FR-018)

- **Decision**: For a running queue, the Edit page uses a template-only editor mode. It reads the entries and the schedule from the linked template. It changes only the template. It does not call the add-entry, remove-entry, or replace-entries calls of the queue.
- **Rationale**: The editor of a stopped queue changes the queue entries first (`addQueueEntry`, `removeQueueEntry`, `replaceQueueEntries`) and then saves the template. `addQueueEntry` and `removeQueueEntry` do not check the run state. A running queue would change at once. This breaks FR-011.
- **Method**: In running mode, the working list of entries lives in the page state. "Save template" sends the entries in order to the template. Entries keep their template entry ID (feature 171). The page does not call `replaceQueueEntries`.
- **Service change**: Add a guard to `POST` and `DELETE` of `{id}/entries` so that both refuse a running queue with 409 `queue_running`. This keeps a bug in the client from changing a running queue.
- **No template**: Show the entries read-only with the message of FR-018. The link call (`PUT {id}/template`) works for a running queue today. A newly linked template can have entries with no IDs. The review then shows "needs upgrade" (feature 171).
- **Alternatives considered**: Reuse the stopped editor and block the calls on the client only: rejected, too easy to break.

## R4b: Enable and disable on the phone and the editor (FR-025, FR-026)

- **Decision**: No new service call. The phone switch uses the existing template save (`toggleTemplateEntry`). The editor shows the `enabled` flag from the template.
- **Rationale**: Both screens already read and write the same template field.

## R5: Review notice on the Edit page and the phone (FR-017, FR-023, FR-014)

- **Decision**: Make one component, `TemplateChangesNotice`, for both screens. It reads `GET {id}/template-diff` for a running queue that has a linked template. It shows "N changes wait" and a Review button only when N is more than 0. The Review button opens the existing `ReviewTemplateChangesDialog`. The dialog gets one explanation sentence (FR-014).
- **Rationale**: `QueuePauseControls` holds the Review button today, in the list row and on the phone. The phone keeps the button, so the component stays shared, with a flag. The big screen list stops using the flag.
- **Refresh**: The notice reads the diff every 5 s with the other data and after each template save.
- **Alternatives considered**: Show the count in the list row: rejected by Q1.

## R6: Pause controls on the Edit page (FR-028)

- **Decision**: Show `QueuePauseControls` on the Edit page of a running queue, above the review notice, without the review flag.
- **Rationale**: The component already shows the state text and the right buttons. It polls the health itself.

## R7: Icon buttons (FR-002 to FR-006, FR-022)

- **Decision**: Add a small `IconButton` component with inline SVG icons (no new dependency). It takes `label`, `icon`, `tone`, and `disabled`. It sets `aria-label` and `title`.
- **Names**: The label text is "<Action> <queue name>" for the accessible name and "<Action>" for the tooltip. Actions: Start, Stop, Pause, Resume, Cancel pause, Edit, Duplicate, Delete.
- **Tap size**: The big screen row uses a 32 px button. The phone does not use this component.
- **Alternatives considered**: An icon font or library: rejected, a new dependency for six icons.

## R8: Schedule button (FR-001)

- **Decision**: Delete the Schedule button, the expanded row, the `liveScheduleFor` state, and the `QueueLiveScheduleControl` use from `QueuesPage`. Keep the service call `liveScheduleSequence` and the component file only if another screen uses them. Search shows no other user, so delete the component and its tests. The service endpoint stays (spec assumption).

## R9: Phone changes (FR-016, FR-021, FR-025 to FR-027)

- **Decision**: (a) The card summary uses the shared activity rule, so it shows "Paused" or "Pausing". (b) The detail shows the shared `TemplateChangesNotice`. (c) The switches stay as they are. After a switch on a running queue, the detail shows the notice after the next refresh. The existing text "Changes apply when the queue starts again" changes for a running queue to the notice text.
- **Docs**: Update `docs/mobile-ui-principles.md` (the summary line and the notice) and `docs/live-template-changes.md`.

## R10: Performance (Constitution IV)

- **Goal**: The poll adds at most one monitor call for each running queue every 5 s. The list does not block on a failed monitor call (edge case). The plan sets no time number for the first load.
