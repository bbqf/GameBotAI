# Implementation Plan: Queue sessions survive idle gaps between scheduled runs

**Branch**: `104-queue-session-idle-eviction` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/104-queue-session-idle-eviction/spec.md`
**Issue**: [#217](https://github.com/bbqf/GameBotAI/issues/217) — closes on merge

## Summary

`SessionManager.CleanupIdleSessions()` removes every session whose `LastActivity` is older than
`Service:Sessions:IdleTimeoutSeconds` (1800 s). A running queue holds one session for its whole run
and touches it only when it fires something, so an idle gap longer than 30 minutes retires the
queue's own session; the queue's next pre-firing check then throws `QueueConnectionLostException`
and the run ends. The fix has two parts plus a message fix:

1. **Root cause** — a session carries an `OwnerQueueId`; `CleanupIdleSessions` skips sessions that
   have one. The queue sets it when it binds its session. Ad-hoc sessions are untouched.
2. **Defense in depth** — every pre-firing session check in `QueueExecutionService.RunAsync` goes
   through one local function that, when the session is missing, makes one attempt to bind a new
   queue-owned session on the same serial (restarting capture) and swaps the run's session id. Only
   a failed re-bind throws `QueueConnectionLostException`.
3. **Screenshot 404 wording** — the `session_not_found` message for `?serial=` says how a session is
   created.

## Technical Context

**Language/Version**: C# 12 / .NET 8
**Primary Dependencies**: ASP.NET Core Minimal APIs, xUnit + FluentAssertions
**Storage**: N/A — sessions are in-memory
**Testing**: `dotnet test GameBot.sln` (unit / integration / contract)
**Target Platform**: Windows service host, localhost REST API on port 8080
**Project Type**: Web service with a shared domain library and an emulator library
**Performance Goals**: No hot-path cost. `CleanupIdleSessions` gains one null check per session per
sweep (≤ 8 sessions). The pre-firing check is unchanged on the happy path (one `GetSession` lookup);
the re-bind path runs only when a session is actually missing.
**Constraints**: `ISessionManager` has ~28 test fakes; the fix MUST NOT change that interface. The
ownership marker therefore lives on the `EmulatorSession` object the manager stores by reference.
**Scale/Scope**: 4 source files, 1 doc, CHANGELOG, STATUS, ~3 test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation
progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|---|---|
| **I. Code Quality Discipline** | PASS. One domain property, one skip condition, one extracted bind helper that removes the duplicated `CreateSession`+`StartCapture` block, and one local function replacing twelve copies of the same guard line. `RunAsync` gets *shorter*, which matters: the build-time taint analyzers degrade on giant methods. New public members get XML docs. No new dependency. CamelCase names only. |
| **II. Testing Standards** | PASS. Bug fix with failing-first tests: a `SessionManager` test that a queue-owned session survives the idle sweep (fails today), and queue tests that a vanished session is re-bound and the firing plus its before-each-run entries run (fail today). The two existing "connection lost" tests are re-expressed as "device gone" (re-bind fails) so they keep pinning FR-004. All deterministic: `LastActivity` is backdated rather than slept on; queue tests use the existing `FakeTimeProvider` harness. |
| **III. UX Consistency** | PASS. The run failure message is unchanged. The screenshot 404 keeps its code and becomes actionable (says how to get a session). The re-bind is logged at Warning with queue, serial, old and new session ids. |
| **IV. Performance** | PASS. See Performance Goals; no perf note beyond this. |
| **V. Living Documentation** | PASS. `docs/architecture.md` gains the session-lifetime rule (queue-owned sessions are not idle-retired; re-bind at firing) with "Last reviewed" refreshed; spec Status set to Implemented and `specs/STATUS.md` row 104 added; CHANGELOG `Fixed` entry. No earlier spec is superseded. |

No violations. **Complexity Tracking** omitted.

Post-Phase-1 re-check: unchanged — no new project, abstraction or interface member.

## Project Structure

### Documentation (this feature)

```text
specs/104-queue-session-idle-eviction/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── session-lifetime-and-rebind.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Sessions/EmulatorSession.cs                       # + OwnerQueueId
├── GameBot.Emulator/Session/SessionManager.cs                       # skip owned sessions in the idle sweep
├── GameBot.Service/
│   ├── Services/QueueExecution/QueueExecutionService.cs             # BindQueueSession helper, EnsureSessionBound, SessionRebound log (QueueExecutionLog class in the same file)
│   └── Endpoints/EmulatorImageEndpoints.cs                          # 404 wording

tests/
├── unit/Sessions/SessionIdleEvictionTests.cs                        # new: owned vs ad-hoc sweep
├── unit/Queues/QueueExecutionServiceTests.cs                        # fake gains Evict(); re-bind tests; T031 re-expressed
├── unit/Queues/QueueExecutionServiceBeforeEachRunTests.cs           # re-bind before BER; T008(g) re-expressed
└── contract/EmulatorScreenshotSelectorContractTests.cs              # extend: 404 message names how to bind a session

docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: No new project. Each change lands in the file that owns the behaviour.

## Implementation Approach

### A — `EmulatorSession.OwnerQueueId` and the idle sweep

Add `public string? OwnerQueueId { get; set; }` to `EmulatorSession` (null = ad-hoc). In
`SessionManager.CleanupIdleSessions`, `continue` when `OwnerQueueId` is non-null. Nothing else in
the manager changes; `StopSession` still removes owned sessions.

### B — One bind path for the queue

Extract the start-of-run block (`CreateSession` → set `handle.SessionId` → `StartCapture`) into a
private `BindQueueSession(ExecutionQueue queue)` that also sets `OwnerQueueId = queue.Id` and
returns the new session id. The start path calls it inside the existing `try/catch
(InvalidOperationException or KeyNotFoundException)`, keeping the existing start failure message.

### C — Re-bind at the pre-firing checks

Inside `RunAsync`, add a local function `EnsureSessionBound()` that:

1. returns if `_sessions.GetSession(sessionId)` is non-null;
2. otherwise stops capture for the old id (best-effort), calls `BindQueueSession(queue)` once
   inside a `try/catch (InvalidOperationException or KeyNotFoundException)`;
3. on success assigns `sessionId` and `handle.SessionId`, logs `SessionRebound`, returns;
4. on failure throws `QueueConnectionLostException` (existing message, FR-004).

Replace every `if (_sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();`
in `RunAsync` with `EnsureSessionBound();`. Because each firing's session argument is evaluated
after the check, the new id flows to the firing and to every later one; the `finally` block stops
whichever session is current. The before-each-run pass's own check is one of the replaced lines, so
a re-bind there lets the before-each-run entries run (FR-006). A wake-up with several due firings
re-binds at most once: after the first re-bind `GetSession` finds the new session.

### D — Screenshot 404

Message becomes: `No running session is bound to device '<serial>'. Start a session
(POST /api/sessions/start) or a queue on that device to bind one.` Code `session_not_found` and
status 404 unchanged.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| A queue whose `finally` never runs leaks an unevictable session | `finally` always runs on normal, faulted and cancelled completion; host shutdown ends the process. The capacity limit still counts it, and `StopSession` still removes it. |
| Existing tests that model a lost device by flipping `Connected=false` now re-bind (the fake's `CreateSession` reconnects) | Re-express them as "device gone": also set `CreateThrows = new KeyNotFoundException(...)`. They keep asserting the unchanged failure message. |
| Re-bind while an ad-hoc session occupies the last capacity slot | Capacity failure is an `InvalidOperationException` → treated as unbindable → run fails with the existing message (spec edge case). |
| `RunAsync` analyzer blow-up | The change removes twelve duplicated lines and adds one short local function; net shorter. |
| `GAMEBOT_USE_ADB` env var in the new `SessionManager` test races with other tests | Same pattern as `SessionCapacityMessageTests` (set/reset in try/finally); the sweep test does not depend on ADB at all. |

## Phase Status

- [x] Phase 0 — research complete ([research.md](./research.md))
- [x] Phase 1 — design complete ([data-model.md](./data-model.md), [contracts/](./contracts/),
      [quickstart.md](./quickstart.md); agent context updated)
- [ ] Phase 2 — tasks (`/speckit-tasks`)
