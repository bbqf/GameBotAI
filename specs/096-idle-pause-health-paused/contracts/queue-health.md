# Contract change: `health` pause fields on `GET /api/queues/{id}`

This change is additive. There are no new endpoints, and `/monitor` and `/resume` are unchanged.

## Idle-paused running queue (before → after)

```json
// before
"health": { "paused": false, "pausedAt": null, "pauseReason": null, "failurePolicyTripped": false }

// after
"health": {
  "paused": true,
  "pausedAt": "2026-09-16T20:57:36+02:00",
  "pauseReason": "idle pause: resumes at 20:59",
  "pauseKind": "idle",
  "failurePolicyTripped": false
}
```

## Failure-policy-paused running queue

```json
"health": {
  "paused": true,
  "pausedAt": "2026-09-14T19:18:02+02:00",
  "pauseReason": "failure policy: 5 consecutive failed cycles",
  "pauseKind": "failurePolicy",
  "failurePolicyTripped": true
}
```

The only difference from today is the added `pauseKind`.

## Not paused

`paused: false`, and `pausedAt`, `pauseReason` and `pauseKind` are all `null`.

## Field descriptions (published on the `QueueHealthResponse` schema by `QueueHealthSchemaFilter`)

- `paused`: whether the run is paused right now, for any reason: an idle pause (routine, ends by itself when the next firing is due) or a failure-policy pause (released only by `POST /api/queues/{id}/resume`). Use `pauseKind` to tell them apart.
- `pausedAt`: when the reported pause began (service-local clock). Null when not paused.
- `pauseReason`: human-readable reason. For an idle pause it is `idle pause: resumes at HH:mm`; for a failure-policy pause it starts with `failure policy:`. Null when not paused.
- `pauseKind`: `idle`, `failurePolicy`, or null when not paused. If both pauses were ever in force at once, the failure-policy pause is reported.

## Compatibility

- Clients that read `paused` as "policy-parked, call resume" must now also check `pauseKind == "failurePolicy"`. The web UI does not read `health.paused`. `/resume` on an idle-paused queue still returns `resumed: false` and changes nothing.
