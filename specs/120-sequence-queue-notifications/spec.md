# Feature Specification: Queue Sequence Notifications

**Feature Branch**: `120-sequence-queue-notifications`  
**Created**: 2026-09-30  
**Status**: Draft  
**Input**: User request (quotation): "I want a notification/alerting mechanism for the sequences in the game. The user configures the targets in the UI/configuration. The user sets the level per queue, so that all sequences that run as part of a queue can send a notification. Sequences that the user runs manually send none. Levels: None / Failure / Success+Failure. Telegram is the first target, but the design must allow more targets. The user creates a Telegram bot by hand and creates a chat with it. The bot ID, the chat ID and other data are stored in the configuration. The UI must guide the user through the bot setup. When a sequence in a queue finishes, the system checks the level of the queue and sends a short message with the queue name, the sequence name and the status, for example `Farm-1 : PNS.CollectResources : success`. Success is green and failure is red, if possible."

## Clarifications

### Session 2026-09-30

- Q: Which finished sequences send a notification? → A: Each sequence that a queue runs as an entry. This includes runs that the queue schedule or reschedule-self books. A sequence that another sequence calls as a step sends no notification. Rationale: the operator thinks in queue entries. Nested sequences would double the messages.
- Q: How is the color shown in Telegram, which cannot color text? → A: The message adds a colored circle symbol before the status. Green is for success and recovered. Red is for failure. Yellow is for cancelled. Rationale: this is the closest plain-text equivalent of the requested colors.
- Q: What identifies a pair for the failure streak? → A: The queue identity and the sequence identity, not their names. A rename keeps the streak. Rationale: a rename must not cause a second "failure" message.
- Q: Does the test message affect queue state? → A: No. The test message does not open or close a failure streak. It needs no queue level. Rationale: it checks the target, not a sequence result.
- Q: What happens when one of several targets cannot receive the message? → A: The other targets still get the message. The system writes each send failure to the log on its own. Rationale: one bad target must not silence the others.
- Q (loop 2, I1): When does level "None" close a streak? → A: When the operator sets the level, not at the next run. Rationale: a later change to "Failure" must start with a closed streak.
- Q (loop 2, C1): How does the system send to several targets? → A: In parallel. Each message has a total limit of 30 seconds. The system writes each dropped message to the log. Rationale: slow targets must not delay SC-002.
- Q (loop 2, C2): Is a hand edit of the queue level supported? → A: No. Only the UI and the API set the level. A hand edit of the targets store works without a restart. Rationale: the queue file is not a supported edit path.
- Q (loop 2, C3/C4/C6): What about target delete, the test text and a queue stop? → A: The list has edit and delete. The test text is "GameBot test message". A queue stop while the host runs sends "cancelled". The system never drops this message. Rationale: a test must check each of these gaps.
- Q (analyze I1): Does level "Failure" send "recovered"? → A: Yes. Both "Failure" and "Success+Failure" send "recovered" after an open failure streak. Rationale: the operator must learn that the fault ended (SC-007).
- Q (analyze A1): What happens to an open streak when the level changes? → A: A change between "Failure" and "Success+Failure" keeps the streak. A change to "None" closes it with no message. Rationale: the operator chose to ignore the fault, so no message is sent.
- Q (analyze U1): How does the operator test unsaved values? → A: A "Save and test" action saves the target. Then it sends the test message. Rationale: one test endpoint that works on a saved target is enough.
- Q (analyze U3): When does the service read hand-edited settings? → A: The service uses a change with no restart (FR-018). Rationale: hand edits must work while the queues run.
- Q: Where are the settings stored? → A: In one persisted store. The UI and the API both read and write it. There is no second copy. Rationale: this satisfies FR-017 and FR-018 with one source of truth.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Get a message when a queue sequence fails (Priority: P1)

The operator runs several queues for a long time and does not look at them. The operator sets a queue to level "Failure". When a sequence in that queue fails, the operator gets a short message on the phone. The message names the queue, the sequence and the status. The operator can react to the problem without the GameBot UI.

**Why this priority**: This is the main value. It removes the need to look at the queues. A failure alert alone makes the feature useful.

**Independent Test**: Configure one target. Set one queue to "Failure". Run a sequence in that queue that fails. Check that one message arrives with the queue name, the sequence name and the status "failure". Check that a sequence that succeeds sends nothing.

**Acceptance Scenarios**:

1. **Given** a target is configured and queue "Farm-1" has level "Failure", **When** sequence "PNS.CollectResources" fails in a run of "Farm-1", **Then** the target receives the message `Farm-1 : PNS.CollectResources : failure`.
2. **Given** the message from scenario 1 goes to a target that supports color, **When** the message arrives, **Then** the status is red.
3. **Given** a target is configured and queue "Farm-1" has level "Failure", **When** a sequence in "Farm-1" succeeds, **Then** no message is sent.
4. **Given** queue "Farm-1" has level "None", **When** any sequence in "Farm-1" finishes with any result, **Then** no message is sent.

---

### User Story 2 - Configure the target and the queue level in the UI, with setup guidance (Priority: P1)

The operator opens a notification settings area in the UI. The operator adds a Telegram target. The UI shows step-by-step instructions. They explain how to create a bot, how to create a chat with the bot, and how to find the values to enter. The operator enters the values and sends a test message. The UI shows if the message arrived. The operator then sets the level (None / Failure / Success+Failure) for each queue.

**Why this priority**: Without a target and a level, no notification can be sent. The user asked for the guide to be part of the UI from the first release.

**Independent Test**: Open the settings area with no target configured. Follow the on-screen guide. Enter the values, send the test message and set a queue level. Check that the settings stay after a service restart.

**Acceptance Scenarios**:

1. **Given** no target is configured, **When** the operator opens the notification settings, **Then** the UI shows the guide to create a Telegram bot and a chat. It also shows the fields for the required values.
2. **Given** the operator entered valid values, **When** the operator selects "Send test message", **Then** a test message arrives at the target. The UI shows that the send worked.
3. **Given** the operator entered wrong values, **When** the operator selects "Send test message", **Then** the UI shows a clear error. The error says that the send failed and, where known, why.
4. **Given** a saved target, **When** the operator opens the settings again, **Then** the UI does not show the secret value (the bot token) in full.
5. **Given** the queue list, **When** the operator changes the level of a queue, **Then** the system saves the new level. It applies to the next sequence that finishes in that queue.

---

### User Story 3 - Get a message for every finished sequence (Priority: P2)

The operator sets a queue to level "Success+Failure". The operator gets one message for each sequence that finishes in that queue. A success message shows the status in green. A failure message shows it in red. This lets the operator confirm that a new queue works as expected.

**Why this priority**: It is useful for new or important queues. It is less critical than failure alerts. It builds on story 1.

**Independent Test**: Set a queue to "Success+Failure". Run one sequence that succeeds and one that fails. Check that two messages arrive. Check that the status colors are green and red.

**Acceptance Scenarios**:

1. **Given** queue "Farm-1" has level "Success+Failure", **When** "PNS.CollectResources" succeeds in "Farm-1", **Then** the target receives `Farm-1 : PNS.CollectResources : success`.
2. **Given** the message from scenario 1 goes to a target that supports color, **When** the message arrives, **Then** the status is green.
3. **Given** queue "Farm-1" has level "Success+Failure", **When** a sequence in "Farm-1" fails, **Then** the target receives a message with the status "failure" in red.

---

### User Story 4 - Manual runs stay silent (Priority: P2)

The operator runs a sequence by hand to test or to debug it. The system sends no notification for this run. This is also true when the same sequence runs in a queue that has notifications on.

**Why this priority**: Manual test runs cause noise. The noise hides the real alerts.

**Independent Test**: Run a sequence by hand while a target is configured and the queues have level "Success+Failure". Check that no message is sent.

**Acceptance Scenarios**:

1. **Given** a target is configured, **When** the operator runs a sequence by hand (outside a queue), **Then** no message is sent.

---

### User Story 5 - Add more target types later (Priority: P3)

A developer can add a new target type (for example Discord or e-mail). The change does not touch queues, levels or message creation. The operator can then select the target type in the settings.

**Why this priority**: The user asked for a design that allows more targets. Only Telegram is needed now. This story limits the design. It needs no separate UI now.

**Independent Test**: Check that the message creation and the level check do not depend on Telegram. Check that a second target type, made for tests only, receives the same messages.

**Acceptance Scenarios**:

1. **Given** two target types are registered, **When** a queue sequence finishes and the level allows it, **Then** both targets receive the message.

---

### Edge Cases

- No target is configured: sequences finish as usual. The system sends no message. The operator sees no error.
- The target is not reachable, or the token or chat is wrong at send time: the send fails. The queue and the sequence continue with no change. The system writes the failure to the service log. The failure does not cause a queue failure.
- The operator cancels a sequence, or a queue stop interrupts it: the message shows the status "cancelled" in yellow (FR-011).
- A sequence stops because of a time limit or a watchdog: the system treats this as a failure.
- The operator deletes or renames a queue: the message uses the current queue name. A rename keeps the failure streak. A deleted queue sends no more messages. The system removes the streak state of a deleted queue (FR-023).
- A sequence that another sequence calls as a step finishes: it sends no message of its own.
- The service restarts: the saved targets and levels stay active.
- A sequence fails many times in a row: the operator gets one "failure" message. The operator gets one "recovered" message when the sequence next succeeds (FR-012).
- The operator changes the level of a queue while a failure streak is open: the streak stays open. A later success sends "recovered" only if the new level allows it.
- The service restarts while a failure streak is open: the streak can be lost. The next failure then sends one new "failure" message. This is acceptable.
- The queue name or the sequence name has characters that the target format treats as special: the message shows the names as plain text and is not broken.
- A queue has no level set: the level is "None".
- The operator changes a queue with a full update, a template reload, a clone or an import: the saved level stays as it was (FR-021). A clone or an import with no level gets "None".
- The operator deletes a sequence: its failure streak can stay in memory until the service restarts. This is accepted. The streak sends no message.
- A queue run, a level change and a queue delete happen at the same time: the streak state stays correct (FR-023).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let the operator set a notification level for each queue. The levels are "None", "Failure", and "Success+Failure". The default level is "None".
- **FR-002**: The system MUST let the operator configure one or more notification targets in one global place. The queue level then applies to all configured targets.
- **FR-003**: The system MUST support a Telegram target. The Telegram target MUST store the bot token and the chat ID. The system MUST store other values that Telegram needs in the same configuration.
- **FR-004**: The system MUST keep target types separate from queue levels and from message creation. A new target type MUST NOT need a change to queue levels or to message creation.
- **FR-005**: These rules apply when a queue entry sequence finishes. A sequence that another sequence calls as a step is not a queue entry. The system MUST check the queue level and the configured targets. It MUST send a notification only if a target is configured and the level allows the result.
- **FR-006**: Level "Failure" MUST send a notification for failed and cancelled sequences (see FR-011). Level "Success+Failure" MUST send a notification for succeeded, failed and cancelled sequences. Both levels MUST send "recovered" (see FR-012 and FR-019). Level "None" MUST send nothing.
- **FR-007**: The system MUST NOT send a notification for a sequence that the operator runs by hand outside a queue.
- **FR-008**: The notification text MUST contain the queue name, the sequence name and the status (success, failure, cancelled or recovered). The format is `<queue name> : <sequence name> : <status>`. The text MUST contain no other information in this version. The only exceptions are the color symbol from FR-009 and the fixed text of the test message from FR-015.
- **FR-009**: Where the target supports color, the system MUST show "success" and "recovered" in green. It MUST show "failure" in red and "cancelled" in yellow. Where the target has no color, the message MUST stay readable as plain text. Telegram cannot color text. For Telegram, the message MUST show a colored circle symbol (green, red or yellow) before the status.
- **FR-010**: A send failure MUST NOT change the result of the sequence or the queue. It MUST NOT stop or slow the queue. The system MUST write the send failure to the service log. If one target fails, the other targets MUST still receive the message. The system MUST send to all targets in parallel. Each message MUST have a total send limit of 30 seconds, also when several targets are slow. If the system drops a message, it MUST write this to the service log.
- **FR-011**: The status "cancelled" applies to a sequence that the operator cancels. It also applies to a sequence that a queue stop interrupts. The system MUST show "cancelled" in yellow where the target supports color. Levels "Failure" and "Success+Failure" MUST send "cancelled" messages. Level "None" MUST send nothing.
- **FR-012**: The system MUST track failure streaks for each pair of queue and sequence. The pair is identified by queue identity and sequence identity, not by name. The streak rules are in the next items.
  - A sequence fails and the pair has no open streak: the system MUST send the "failure" message and open a streak.
  - The streak is open: further failures of the same pair MUST NOT send messages.
  - The sequence next succeeds: the system MUST close the streak. It MUST send a "recovered" message instead of a "success" message.
  - The system MUST send "recovered" only if it sent the "failure" message that opened the streak.
  - A "cancelled" result MUST NOT open, extend or close a streak.
  - The level is "None": the system MUST send nothing and MUST NOT open a streak.
  - The operator sets the level to "None": the system MUST close an open streak at once, with no message. It MUST NOT wait for the next run.
  - The operator changes the level between "Failure" and "Success+Failure": the streak MUST stay open.
- **FR-013**: The UI MUST have a notification settings area. There the operator sees a list of the targets. The operator can add, change and remove each target from this list. The operator can set the level for each queue. A save with an empty token field MUST keep the stored token.
- **FR-014**: The UI MUST show a step-by-step guide for the Telegram setup. The guide MUST explain how to create a bot and how to get its token. It MUST explain how to start a chat with the bot and how to find the chat ID. It MUST show which value goes into which field.
- **FR-015**: The UI MUST offer a "Send test message" action for each target. The test message MUST NOT change any failure streak. The test message has the fixed text "GameBot test message". The action MUST show the operator if the send worked. If the send failed, it MUST show a clear reason where the target gives one.
- **FR-016**: The system MUST NOT show the bot token in full after the operator saves it. The UI and the API responses MUST hide or mask it. The operator MUST be able to replace it.
- **FR-017**: The system MUST keep targets and queue levels after a service restart. It MUST keep them in one store that the UI and the API both use.
- **FR-018**: The same settings MUST be available through configuration and through the UI. The operator can then set them without the UI. The system MUST use a change to the targets store with no service restart. Only the UI or the API sets the queue level. A hand edit of the queue file is out of scope.
- **FR-019**: While a failure streak is open, the first success MUST send "recovered" at level "Failure" and at level "Success+Failure". Later successes MUST send "success" at level "Success+Failure" only.
- **FR-020**: The UI MUST let the operator test a target before the operator leaves the form. The "Save and test" action MUST save the target. Then it MUST send the test message.
- **FR-021**: The notification level of a queue MUST survive all other queue changes. These changes are a full queue update, a template edit with reload, a clone and an import. An update that does not name the level MUST keep the saved level. Only the level action of the operator (UI or API) MAY change the level. A clone or an import with no level MUST set the level to "None".
- **FR-022**: When the work ends, the Status line of this spec MUST change to "Implemented". The entry for this feature in `specs/STATUS.md` MUST also change to "Implemented". This keeps the living docs true (constitution Principle V).
- **FR-023**: The system MUST change the failure streak state safely when queue runs, level changes and queue deletes happen at the same time. There MUST be no data race. The cleanup on a queue delete and on the level "None" MUST NOT race with the send worker.

### Key Entities

- **Notification Target**: A place that receives messages. It has a type (for example Telegram), a name, an enabled state and the type-specific values (for Telegram: the bot token and the chat ID). It is global, not per queue.
- **Queue Notification Level**: A setting on a queue with one of three values: None, Failure, Success+Failure.
- **Notification Message**: The short text for one finished sequence. It has a queue name, a sequence name and a status (success, failure, cancelled or recovered).
- **Failure Streak**: The state for one pair of queue and sequence after a "failure" message was sent. It stays open until the sequence next succeeds.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time operator can set up a Telegram target and receive a test message in under 10 minutes. The operator uses only the on-screen guide.
- **SC-002**: A sequence in a queue with level "Failure" fails. The operator receives the message within 30 seconds of the failure, in 95% of cases where the target is reachable.
- **SC-003**: A test run uses all three levels. The number of messages is 100% correct: none for "None", failures only for "Failure", and all results for "Success+Failure".
- **SC-004**: Manual sequence runs cause 0 notifications.
- **SC-005**: When the target is not reachable, queue run time and queue results are the same as when no target exists.
- **SC-006**: The bot token is not visible in full in any UI screen or API response after it is saved.
- **SC-007**: One sequence fails 10 times in a row and then succeeds. The operator receives exactly 2 messages at level "Failure" and at level "Success+Failure": one "failure" and one "recovered".

## Assumptions

- The targets are global. All queues that have a level other than "None" send to all enabled targets. The user did not ask for a target choice per queue.
- The operator creates the Telegram bot and the chat by hand. The system does not create bots.
- The message is one line with the format from FR-008. Extra data (duration, errors, screenshots) is out of scope for this version.
- A "failure" is a sequence that ends with an error. It is also a sequence that stops because of a time limit or a watchdog.
- Messages go one way only. The bot does not receive commands or replies.
- The only target type built in this version is Telegram. Other types are out of scope, but the design must allow them.
- Each verification item in the plan and tasks MUST name the exact check and the pass criterion.
- The final quickstart run MUST include a timing check for SC-001 and SC-002.
- The send retry budget (number of attempts times the time limit of each attempt) MUST fit in 30 seconds. This supports SC-002.
- No real Telegram bot or phone is available: the real-target checks then use a fake target. The team records this in the pull request. These checks do not block the final status update.
- A time limit or a watchdog stop counts as "failure". A named test checks it. A queue stop while the host runs counts as "cancelled".
- Specs, plans, tasks and docs for this feature use ASD-STE100 Simplified Technical English (constitution Principle VI).
- The plan and tasks steps MUST pass an STE check before the step ends. Each sentence MUST be within the limits: 25 words for a description and 20 words for a procedure. No "-ing" forms are allowed, except in names and quotations.
- Task rules: two tasks MUST NOT be marked [P] if they write the same file. A test task MUST come before the code it checks, where the change is new. A task MUST NOT say "fix any gap" without a named check. The plan MUST name the entry point for the streak cleanup (FR-023).
- The new modules MUST reach the project coverage baseline of 80% line and 70% branch (constitution Principle II). A task MUST measure this coverage.
- The plan MUST design the hand-off of the streak state between the queue run, the level change, the queue delete and the send worker (FR-023).
