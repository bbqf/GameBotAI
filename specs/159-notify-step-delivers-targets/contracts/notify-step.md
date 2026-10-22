# Contract: `notify` step destination

The sequence JSON shape of `notify` does not change. Only the meaning of the default destination changes.

## Payload (unchanged)

```json
{ "type": "notify", "message": "Quest A is ready", "url": "https://example.test/hook" }
```

- `message`: string, required, at most 1000 characters.
- `url`: optional absolute http or https URL.
- `dedupeKey`, `since`, `within`: unchanged (feature 137).

## Destination rules

| `url` | Enabled targets | Result |
|-------|-----------------|--------|
| set | any | The message goes to `url` only. No target gets it. |
| not set | one or more | The message goes to each enabled target, one time each. The text is `message` with no prefix. |
| not set | none | The old path: `Service:Notifications:DefaultUrl`, else the log "no destination configured". The step stays `success`. |

## Wait limit and partial failure

The step waits at most 35 s (worker `SendTimeout` 30 s plus a 5 s margin). After that the result is "not delivered" and the step stays `success`. When at least one target accepts, the result is "delivered".

## Step outcome and messages (unchanged words)

- Delivered: `success` (or `sent` with a dedupe reservation), message "notification delivered".
- Not delivered: `success`, message "notification not delivered: <reason>".
- Dedupe window used: `suppressed`.
- Step-through: `preview`, no send. The target path is not used in step-through mode (FR-014).

## OpenAPI text (FR-008)

The `notify` description and its `url` part MUST say:

- When `url` is not set, the message goes to the enabled notification targets.
- The targets are the ones that `GET /api/notifications/targets` lists.
- When `url` is set, the message goes only to that URL.
- `Service:Notifications:DefaultUrl` is used only when no target is enabled.

A contract test reads the published OpenAPI document and checks that the text names the enabled notification targets.

## Not changed

- `GET/POST/PUT/DELETE /api/notifications/targets` routes and bodies.
- Queue notification level messages and their format.
- Queue alerts (feature 121).
