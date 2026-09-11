# API Contract Changes: Fix Sequence & Session-Input API Bugs

All changes below are additive/tightening only — no route is removed or
renamed, and every well-formed existing request continues to behave exactly as
it does today (spec FR-004, FR-008, FR-012).

## POST /api/sequences, PUT /api/sequences/{id} — B-003

**Before**: A `Command`-typed action step (top-level or nested in a `Loop`/`If`
body) whose payload has no `commandId` is accepted with `201`/`204`; the created
sequence silently dispatches to a command whose id equals the step's own
`stepId`, which fails at run time.

**After**: Such a step is rejected at creation/replace time.

```text
400 Bad Request
{
  "message": "Invalid sequence payload",
  "errors": [
    "step 'tap-1' (type Command) requires a non-empty 'commandId' in its payload"
  ]
}
```

This reuses the existing `{ message, errors }` shape already returned by
`SequencesEndpoints.cs` for other per-step validation failures — no new error
envelope.

**Unchanged**: a step whose payload has a valid `commandId` (with or without an
additional `commandName` key, which is ignored as it is today) is accepted exactly
as before.

## Sequence execution — `requireDispatch` on nested steps — B-005

Not a route/contract change — the request and response *shapes* for
`POST /api/sequences` and sequence-run status endpoints are unchanged. What
changes is the *execution outcome*: a nested step with `requireDispatch: true`
whose action does not dispatch now causes that step, and the run, to be reported
as `Failed` (via the same existing execution-status fields), instead of
`Succeeded`.

## POST /api/sessions/{id}/inputs — B-001

**Before**:

```text
409 Conflict   (misleading — session may be perfectly healthy)
{ "error": { "code": "not_running", "message": "Session not running.", "hint": null } }
```

returned whenever every posted action failed to parse/dispatch, regardless of
actual session status.

**After** — three distinguishable outcomes:

1. Session genuinely not found / not running (unchanged):

   ```text
   409 Conflict
   { "error": { "code": "not_running", "message": "Session not running.", "hint": null } }
   ```

2. Session running, but **none** of the posted actions could be dispatched (new):

   ```text
   400 Bad Request
   {
     "error": {
       "code": "invalid_input_actions",
       "message": "No posted actions could be dispatched.",
       "hint": null
     },
     "results": [
       { "index": 0, "dispatched": false, "failureReason": "swipe: missing required argument 'x1'" }
     ]
   }
   ```

3. Session running, **some or all** actions dispatched (existing status code,
   extended body):

   ```text
   202 Accepted
   {
     "accepted": 1,
     "results": [
       { "index": 0, "dispatched": true,  "failureReason": null },
       { "index": 1, "dispatched": false, "failureReason": "swipe: missing required argument 'y2'" }
     ]
   }
   ```

**Unchanged**: a request where every action is well-formed continues to return
`202 { "accepted": N }` (now additionally carrying a fully-`dispatched: true`
`results` array — additive, not breaking, for callers that only read `accepted`).
