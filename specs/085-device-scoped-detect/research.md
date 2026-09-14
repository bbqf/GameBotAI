# Phase 0 Research: Device-Scoped Image Detection

**Feature**: 085-device-scoped-detect | **Date**: 2026-09-14

All Technical Context unknowns were resolvable from the codebase; no external research was needed.
The findings below are the ones that actually shaped the design.

## R1 — Where exactly the empty result is manufactured

**Finding**: `ImageDetectionsEndpoints.DetectAsync` has two `Results.Ok(new DetectResponse { Matches
= new(), LimitsHit = false })` returns before any matching happens — one when `IScreenSource` is
absent from DI, one when `GetLatestScreenshot()` returns null. Neither is reached through the
matcher, so neither represents a measurement.

**Decision**: Both become explicit errors. The bug is entirely these two lines' interpretation of a
missing frame; nothing below the endpoint is at fault.

**Alternatives considered**: Adding a `measured: true/false` flag to the 200 response. Rejected — it
keeps a failure on the success channel, so every existing caller that does not know to read the new
flag stays broken in exactly the way the issue describes. A silent-by-default fix for a
silent-failure bug is not a fix.

## R2 — Why the resolver returns null (and why it should keep doing so)

**Finding**: `BackgroundCaptureScreenSource.ResolveSessionId()` (feature 079) resolves ambient
`DeviceContext` → sole running session → `null`. Its XML doc states the intent outright: return
nothing rather than pick a device arbitrarily, because the pre-079 behaviour "silently gave one
queue run another run's screen".

**Decision**: Leave it untouched. It is already right; it is the caller that discards the
distinction. This matches the spec's Out of Scope list.

**Alternatives considered**: Having the resolver throw a typed exception instead of returning null,
so callers cannot ignore it. Rejected for this feature — it would change behaviour for every trigger
evaluator, condition adapter and the standalone trigger worker, all explicitly out of scope, to fix
one caller.

## R3 — Precedent for the error vocabulary

**Finding**: `GET /api/emulator/screenshot` (`EmulatorImageEndpoints.cs`) already solves precisely
this problem, introduced by the same feature 079:

| Condition | Status | Code |
|-----------|--------|------|
| `serial` given, no session bound to it | 404 | `session_not_found` |
| no selector, >1 running session | 409 | `ambiguous_session` |
| no session resolved / capture failed | 503 | `emulator_unavailable` |

It accepts `sessionId` **or** `serial` as selectors, and `docs/architecture.md` already documents
the `409 ambiguous_session` behaviour.

**Decision**: Reuse these three codes and their status codes verbatim. A caller that already handles
them on the screenshot route handles them here with no new code.

**Alternatives considered**: New detect-specific codes (`detect_ambiguous_device`, etc.). Rejected —
gratuitous vocabulary growth for an identical condition, and it would make the two routes disagree
about the same underlying situation.

## R4 — Which selector to accept: `serial`, `sessionId`, or `captureId`

**Finding**: Three identifiers are in play. `captureId` is what `/api/images/detect-all` takes and
what `GET /api/emulator/screenshot` hands back in its `X-Capture-Id` header, so the
screenshot → crop → detect authoring loop already has one in hand. `sessionId` is the system's
primary session identity and what `IScreenSourceFactory.ForSession` consumes directly. `serial` is a
device address that `EmulatorImageEndpoints` resolves to a session via a private helper.

**Decision**: Accept `captureId` and `sessionId`. Omit `serial`.

**Rationale**: `captureId` is the precise answer to the absence-probe use case in the issue — it
measures *the exact frame the caller already inspected*, eliminating the race between capturing a
screen and asking about it. `sessionId` covers "just look at that emulator now" without making the
caller manage captures. `serial` would need its own lookup helper duplicated into this endpoint and
adds a third way to say something the other two already say; it can be added later without a
breaking change if a caller actually wants it.

**Alternatives considered**: `captureId` only (as the issue's suggested fix reads). Rejected — it
forces every probe to take a screenshot first, and `CaptureSessionStore` holds only 10 entries with
LRU trimming, so a busy multi-emulator observer could have its capture evicted between taking and
using it. `sessionId` gives such callers a path with no store dependency at all.

## R5 — The constraint that decides whether existing tests survive

**Finding**: `IScreenSource` is registered in two shapes. With `GAMEBOT_USE_ADB` unset (production,
Windows) it is `BackgroundCaptureScreenSource`, which returns null when ambiguous. With
`GAMEBOT_USE_ADB=false` (every contract test) it is a `SingleBitmapScreenSource` built from
`GAMEBOT_TEST_SCREEN_IMAGE_B64`, which **always returns a frame regardless of how many sessions
exist — including zero**. Off Windows, neither is registered at all, but CI runs `windows-latest`.

**Decision**: Detect the failure from the null frame, never from a session count taken beforehand.
Consult `ISessionManager` only after a null frame, and only to pick between `409` and `503`.

**Why this matters**: the intuitive implementation — "if `ListSessions().Count > 1` return 409, else
proceed" — reads plausibly and passes review, but `DetectImageTests` runs with **zero** sessions and
a stub frame. Any variant that checks session state before asking for a frame turns those green
tests red while changing nothing about the real defect. This finding is promoted to spec **FR-016**
because it is a behavioural requirement, not an implementation detail: it is what lets FR-007
(explicit errors) and FR-013 (no change for existing callers) both hold.

**Alternatives considered**: Special-casing stub mode with an environment check inside the handler.
Rejected — production behaviour would then diverge from what the tests exercise, which is how this
class of bug is born in the first place.

## R6 — Constitution-mandated documentation surface

**Finding**: Constitution principle V requires `docs/architecture.md` to be updated in the same PR
for any API-surface change, with a refreshed "Last reviewed" date, and requires each spec to carry
an accurate `**Status**:` line with `specs/STATUS.md` kept consistent.

**Decision**: Treat these as first-class tasks, not cleanup. The architecture doc's device-resolution
bullet list already names the screenshot endpoint's `409 ambiguous_session`; the detect endpoint
gets an adjacent bullet in the same list so the two read as one rule.

## R7 — Analyzer cost in this file

**Finding**: `ImageDetectionsEndpoints.cs` opens with a comment explaining that handlers are named
static methods rather than lambdas because the Roslyn taint analyzers (CA3xxx) fold lambdas into the
containing method and their cost grows super-linearly with body size. The repo has a known history
of build-time analyzer blowups on large methods.

**Decision**: Put frame resolution in its own named static method returning a small outcome type,
rather than inlining ~40 lines of branching into `DetectAsync`. This follows the file's stated
convention and keeps each method small enough to stay cheap to analyze — and it makes the four
outcomes unit-testable without spinning up the host.
