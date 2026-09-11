# Phase 1 Data Model: Dry-Run / Validate-Only Sequence Mode

No new persisted entities and no change to any stored sequence-definition schema
— `dryRun` is a per-request flag, never written to a sequence's own file/record.
This feature touches request contracts, one new canonical outcome constant, and
the runtime outcome map already introduced by prior features.

## `SequenceUpsertContract` (existing, `GameBot.Service.Models`, extended)

| Field | Type | Meaning |
|-------|------|---------|
| `DryRun` | `bool?` (JSON `dryRun`), default `false`/absent | When `true` on `POST /api/sequences` (per-step request shape only — see [research.md](research.md) Unknown 1), run identical enrichment + validation but do not persist. |

No existing field changes meaning. Absent/`false` reproduces today's create
behavior exactly (FR-011).

## `SequenceExecuteContract` (existing, `GameBot.Service.Models`, extended)

| Field | Type | Meaning |
|-------|------|---------|
| `DryRun` | `bool?` (JSON `dryRun`), default `false`/absent | When `true` on `POST /api/sequences/{id}/execute`, walk the sequence's real step tree without dispatching to the emulator, starting a session, or reading live capture state. |

`SessionId` remains optional and, when `dryRun` is `true`, is never required to
resolve to a running session (FR-009) — no code change needed to make it
"optional under dry-run" specifically, since the dry-run leaf-dispatch gate (see
[research.md](research.md) Unknown 2) means session resolution is simply never
reached for a dry-run leaf step.

## `DryRunOutcomes` (new, `GameBot.Domain.Services`)

A canonical-constant holder alongside the existing `BreakOutcomes`
(`SequenceRunner.cs:23-32`):

| Constant | Value | Meaning |
|---|---|---|
| `DryRunOutcomes.SkippedDryRun` | `"skipped_dry_run"` | The step would have dispatched input to the emulator, started/used a session, or read live capture state, but did not because `dryRun` was `true`. |

Used as `StepResult.ActionOutcome` for every step the dry-run leaf-dispatch gate
intercepts (primitive tap/swipe/key, connect-to-game, ensure-game-running,
go-to-home-screen, ensure-emulator-running, wait-for-image, reschedule-self, and
any command-referencing step) and as the value written into `stepOutcomes` for
that step's `StepId`, so it is resolvable — but never matching — from a
`commandOutcome` reference elsewhere in the sequence (see Edge Cases in
[spec.md](spec.md)). **Not** added to `SequenceStepValidationService`'s
`AllowedCommandOutcomeStates` — an author cannot legally write a condition
expecting `skipped_dry_run`; it only ever appears as an actual runtime value.

## `CommandDispatchOutcome` (existing, `GameBot.Domain.Services`, extended)

| Field | Type | Meaning |
|-------|------|---------|
| `Dispatched` | `bool` | Existing — whether the command actually put input on the device. |
| `Reason` | `string?` | Existing — why not, when `Dispatched` is `false`. |
| `SkippedDryRun` | `bool`, default `false` | New. `true` when a command-referencing step's `commandId` resolved to a real command but was not dispatched because `dryRun` was `true` — distinct from a genuine "nothing dispatched" miss, so it MUST NOT trip the existing `RequireDispatch` failure check. |

An unresolvable `commandId` is unaffected by this addition — it still throws before any
`CommandDispatchOutcome` is constructed (FR-010), dry-run or not.

## `StepResult` (existing, no shape change)

No new field. `ActionOutcome` (already a free-form `string`) carries
`"skipped_dry_run"` under dry-run exactly as it carries `"executed"`/`"failed"`/
`"break"`/`"no_break"`/etc. today; `Status` is `"Succeeded"` for a dry-run-skipped
step (it is an intentional, successful no-op, not a failure) — the same
`"Succeeded"` status a real dispatch, a `no_break`, or a taken `If` branch
already uses.

## Runtime outcome map (`stepOutcomes: Dictionary<string, string>`, existing)

No shape change. A dry-run-skipped step writes `DryRunOutcomes.SkippedDryRun`
under its `StepId` instead of `"success"`/`"failed"`/etc. Since
`AllowedCommandOutcomeStates` never includes `"skipped_dry_run"`, any
`commandOutcome` condition elsewhere in the sequence referencing that step
resolves its equality check to `false` regardless of `ExpectedState` — a
dry-run-skipped step's outcome can never satisfy a real branching condition.

## `ISequenceExecutionService.ExecuteAsync` / `SequenceRunner.ExecuteAsync` (existing, extended)

Both gain one new optional parameter, `bool dryRun = false`, placed with the
existing optional parameters (after `scope`, before `ct`) — the same pattern
feature 078 used to add `scope` itself. Purely additive: every existing call
site (including `QueueExecutionService`, which never passes it) compiles
unchanged and keeps `dryRun` at its default `false` (FR-012).
