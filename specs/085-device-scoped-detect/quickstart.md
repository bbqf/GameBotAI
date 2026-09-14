# Quickstart: Device-Scoped Image Detection

**Feature**: 085-device-scoped-detect | **Date**: 2026-09-14

How to use `POST /api/images/detect` once this feature ships. The service listens on **port 8080**;
all `/api/*` routes need a bearer token.

> Identifiers in the examples below (`sess_5558`, `cap_abc123`) are **placeholders**. Real session
> ids come from `GET /api/sessions` and are not derived from the emulator serial; real capture ids
> come back in the `X-Capture-Id` response header of `GET /api/emulator/screenshot`.

## The one-line summary

If more than one emulator may be running, **name the screen you mean** — or be ready to handle a
`409`. An empty `matches` array now means "measured, found nothing", and nothing else.

## Absence probes — the case this feature exists for

An absence probe asks "is this dialog *gone*?" and needs a low threshold, so that "present but
faint" is reported as a low score rather than suppressed. Pair a screenshot with a detect against
that same frame:

```bash
curl -s -D- -o frame.png -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/emulator/screenshot?sessionId=sess_5558"
```

The response carries an `X-Capture-Id` header. Feed it straight back:

```bash
curl -s -X POST "http://localhost:8080/api/images/detect" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"referenceImageId":"pns-quit-cancel","threshold":0.3,"captureId":"cap_abc123"}'
```

Measuring the *same frame* you inspected removes the race between capturing a screen and asking
about it — the screen cannot change in between.

> `CaptureSessionStore` keeps only the **10 most recent** captures. Use a `captureId` promptly; a
> trimmed one returns `404 capture_not_found` rather than quietly measuring something else.

## Just look at that emulator now

When you do not need a specific frame, name the session and skip capture management entirely:

```bash
curl -s -X POST "http://localhost:8080/api/images/detect" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"referenceImageId":"pns-city-anchor","threshold":0.3,"sessionId":"sess_5558"}'
```

Supplying **both** `captureId` and `sessionId` is a `400` — the service will not choose for you.

## Single-emulator callers: nothing to do

With exactly one session running, omit both fields and everything behaves as it always has:

```bash
curl -s -X POST "http://localhost:8080/api/images/detect" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"referenceImageId":"pns-city-anchor"}'
```

Inside a running sequence or queue, the ambient device context already identifies the device, so
steps keep resolving correctly even with several emulators running.

## Reading the responses

| You get | It means | Do |
|---|---|---|
| `200`, `matches` non-empty | Found, with scores | Use the score |
| `200`, `matches` empty | **Measured**; nothing at or above `threshold` | Trust it as a real absence |
| `400 invalid_request` | Malformed — bad range, or both targets named | Fix the request |
| `404 not_found` | No such `referenceImageId` | Check the image id |
| `404 capture_not_found` | Capture unknown or trimmed | Take a fresh screenshot |
| `404 session_not_found` | No such session | Check the session id |
| `409 ambiguous_session` | Several emulators; you named none | Add `sessionId` or `captureId` |
| `503 emulator_unavailable` | No screen obtainable at all | Start the emulator; retry |

**For watchdogs and classifiers**: treat every non-2xx as **indeterminate**, never as `0.0`. Mapping
an error to a zero score is precisely the bug this feature fixes — the old code did it on the
service side, and a client that re-does it on its own side is no better off.

## Migrating an existing multi-emulator observer

1. Anywhere a helper converts "no matches" to `0.0`, first branch on the status code. Non-2xx must
   not become a score.
2. Add `sessionId` (or `captureId`) to probes that run while more than one emulator may be up.
3. Re-run the probe with two emulators started. Before this feature it silently returned `0.0` for
   everything; it must now either return real scores or fail loudly.

## Verifying the fix by hand

```bash
# Two sessions running, no target named -> 409 (previously: 200 with an empty array)
curl -s -o /dev/null -w '%{http_code}\n' -X POST "http://localhost:8080/api/images/detect" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"referenceImageId":"pns-city-anchor"}'

# Same two sessions, target named -> 200 with real scores
curl -s -X POST "http://localhost:8080/api/images/detect" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"referenceImageId":"pns-city-anchor","sessionId":"sess_5558"}'
```
