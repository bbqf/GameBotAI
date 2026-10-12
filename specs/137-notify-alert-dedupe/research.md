# Research: Notify alert dedupe window

No item in the Technical Context needed clarification. The decisions below fix the design choices.

## R-001: Where the record lives

- Decision: A new store `ISentAlertStore` with a file-backed class `FileSentAlertStore`. One file: `<dataRoot>/notify-alert-dedupe/sent-alerts.json`.
- Rationale: The record must survive a restart (FR-008). `FileSequenceRunStatisticsStore` already shows a safe pattern: one gate, an in-memory copy, a temporary file, then an atomic replace. A damaged file gives an empty record set and a warning.
- Alternatives: (a) Reuse `FileSequenceRunStatisticsStore`. Rejected: that store is keyed by sequence, and the dedupe scope is (queue, key). A queue delete would also need care. (b) Keep the records in memory only. Rejected: it breaks FR-008 and SC-005. (c) One file for each queue. Rejected: the number of records is small, and one file is simpler. Prune runs on each write.

## R-002: Reservation before delivery

- Decision: `TryReserve(queueId, key, windowStart, now)` runs under the gate. It returns `Suppressed` when a committed record has `sentAt >= windowStart`, or when a live reservation exists. Otherwise it stores an in-memory reservation and returns a token. `Commit(token, sentAt)` writes the record. `Release(token)` drops the reservation.
- Rationale: This keeps FR-007 (record only on success) and FR-013 (one send for concurrent runs). A reservation is not written to disk, so a crash during delivery leaves no false record.
- Reservation expiry: A reservation older than 5 minutes counts as dead. The notifier has its own time-out and attempt limit, so 5 minutes is more than one delivery. This stops a lost token from blocking alerts for ever.
- Alternatives: Check, then send, then record. Rejected: two concurrent runs can both pass the check.

## R-003: Window math

- Decision: `since` uses `LastRunWindow.SinceStart(now, since, timeProvider.LocalTimeZone)`. `within` uses `now - within`. The check is `sentAt >= windowStart`.
- Rationale: FR-005 and SC-004 require the same rule as `lastRun`. A direct call to the same function gives that without a copy.
- Alternatives: A new window class. Rejected: it duplicates daylight-saving logic.

## R-004: Time source

- Decision: Inject `TimeProvider` into the dispatch path (optional parameter, default `TimeProvider.System`). The store receives `now` from the caller.
- Rationale: Tests can fix the clock. The store has no clock, so it is simple to test.

## R-005: Dedupe scope key

- Decision: Scope is (queueId, key), with the queue id as the identity. An ad hoc run (no queue) uses the fixed scope id `adhoc`. Key compare is ordinal and case-sensitive after trim.
- Rationale: Spec clarification and the Assumptions. The `adhoc` scope cannot clash with a queue id because queue ids are generated GUID text.

## R-005a: Scope input in the base flow

- Decision: `DispatchNotifyAsync` builds the scope (queue identity, or `adhoc`) in the same code path as the dedupe check. It is not a separate later step.
- Rationale: The check cannot run without a scope. Spec Assumptions state this.

## R-006: Pruning

- Decision: On each commit, remove records with `sentAt` older than 48 hours. Also remove all records of a queue when the queue is deleted (call from the same place that deletes queue statistics).
- Rationale: The longest window is 24 hours (`within`), or about 24 hours plus a daylight-saving change (`since`). 48 hours is safe and bounds the file (FR-009).
- Alternatives: A timer-based sweep. Rejected: a commit-time prune needs no new service.

## R-007: Validation

- Decision: `NotifyPayload.TryRead` parses the new fields and returns one message per problem. The save-time validator and the dispatch both already call `TryRead`, so the rules are in one place. Rules: `dedupeKey` not blank and at most 100 characters; `since` matches `HH:mm` (`TimeOnly.TryParseExact`); `within` matches `HH:mm:ss` and is from 00:00:01 to 24:00:00; `since` and `within` are exclusive; a key needs a window and a window needs a key. `24:00:00` is a special case because `TimeSpan` parsing accepts it as 24 hours with the `c` format.
- Rationale: Reuse of the existing path gives a 400 response with no new endpoint code (FR-011, SC-003).
- Open point checked: The existing sequence save path returns 400 for payload errors from `ValidateNotifyPayload`. A contract test confirms it.

## R-008: Outcome text

- Decision: With `dedupeKey`, the result outcome is `sent` or `suppressed`, and the status stays `success`. A delivery failure keeps today's text (`notification not delivered: ...`) with outcome `success`. A step without the new fields keeps `success` and the same text.
- Rationale: FR-004, FR-010 and the clarification. A failed delivery is not `sent`, so the outcome for it stays as before.

## R-009: Step-through preview

- Decision: The preview reads the store with a read-only call `WouldSuppress(queueId, key, windowStart)`. It never reserves and never writes. The preview text says `would send a notification` or `would suppress the notification (sent at HH:mm)`.
- Scope rule: Preview uses the same scope as a real run (the queue identity of the stepped run, or `adhoc`). A live reservation also shows `would suppress the notification` (read-only call `HasLiveReservationAsync`).
- Rationale: FR-014. Preview must predict the real result.

## R-011: Null store

- Decision: The `ISentAlertStore` parameter of `SequenceExecutionService` is optional. When it is null, a step with dedupe fields always sends (outcome `sent`), and the preview says `would send a notification`.
- Rationale: Spec Assumptions. Hand-built test and tool instances keep working.

## R-012: Chain test size

- Decision: The main integration test runs the step 10 times in one window and expects 1 `sent`, 9 `suppressed`, and 1 alert at the notifier. A shorter 3-run test also stays.
- Rationale: SC-001 names 10 retries.

## R-010: OpenAPI

- Decision: Extend the `PayloadDescriptions[ActionTypes.Notify]` text with the three fields and the two outcome values.
- Rationale: FR-012. The schema filter is the place where all step payloads are described.
