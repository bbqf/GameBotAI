# Research: Honour dryRun on sequence updates and reject unresolvable command references

## R-001 — Why PUT ignores `dryRun`

**Finding**: `SequencesEndpoints.CreateSequenceAsync` checks `perStepRequest.DryRun` just before
`repo.CreateAsync`. `UpdateSequenceAsync` and `PatchSequenceAsync` deserialize the same
`SequenceUpsertContract` (which carries `DryRun`) through `TryReadPerStepRequest` but never read the
property; they fall through to `existing.Version += 1` and `repo.UpdateAsync`. PATCH has the
identical defect.

**Decision**: short-circuit both update handlers immediately before the version bump, reading
`dryRun` from the raw JSON root.
**Rationale**: every error return (`404`, `409`, `400` structural/image/parameter) already precedes
that line, so dry run and real write fail identically without duplicating any check. Reading the raw
root covers legacy body shapes, which never deserialize to `SequenceUpsertContract`.
**Alternatives considered**: `400` rejecting `dryRun` on update (issue's second option) — rejected
because callers reasonably expect symmetry with create; reading `perStepRequest.DryRun` only —
rejected because a legacy-shape body would still be silently applied.

## R-002 — Is a dry run's in-memory mutation safe?

**Finding**: `FileSequenceRepository.GetAsync` deserializes a new `CommandSequence` from disk on
every call; nothing is cached or shared. The update handlers mutate that private copy.
**Decision**: no defensive clone needed; skipping `UpdateAsync` leaves storage untouched.

## R-003 — Why a nonexistent `commandId` is accepted

**Finding**: `EnrichCommandReferencesAsync` builds a case-insensitive id→name lookup from
`ICommandRepository.ListAsync` and only annotates each command step's `CommandReference` (a null name
when the id is unknown). `ValidatePerStepForPersistenceAsync` checks structure, image references and
action types but never command existence. The B-003 check in `SequenceStepValidationService` only
requires a *non-empty* `commandId`. `ToSequenceResponse` then computes `isResolved` on read.

**Decision**: add an existence pass to the shared per-step validation, reusing the lookup enrichment
already built (enrichment returns it).
**Rationale**: one place, applied identically by create/update/patch and by dry run; zero added I/O.
**Alternatives considered**: putting the check in `SequenceStepValidationService` (domain) — rejected:
that service is synchronous and repository-free; injecting a repository widens its contract for one
rule. The sibling image-reference existence check already lives in the endpoint for the same reason.

## R-004 — Who else writes sequences

**Finding**: `BackupService` restore calls `ISequenceRepository.CreateAsync` directly, not the HTTP
endpoint; queue/template code only reads sequences. The web UI chooses commands from a list of
existing commands.
**Decision**: only the three HTTP write routes are affected; restore of an archive whose sequences
reference commands absent from the archive is unchanged (non-goal).

## R-005 — Deleted-command re-save and defect-dependent fixtures

**Finding**: feature 041 deliberately supports a sequence whose command was deleted after save:
GET shows `isResolved: false` with the snapshot name and a round-trip save keeps it
(`SequenceMissingCommandReferenceIntegrationTests`, via PATCH). A blanket rejection would break it.

**Decision**: on update/patch, tolerate any unresolved id already referenced anywhere in the stored
sequence (matched by id, case-insensitive, not by step id, so reordering/relabelling during an edit
still saves). Create has no stored sequence, so nothing is tolerated.

**Finding**: fixtures that POST sequences referencing commands never created (they rely on the
defect): `IfStepContractTests` (`cmd-close`, `cmd-continue`), `SequencePerStepConditionsContractTests`
(`cmd-mail`), `PrimitiveActionContractsTests` (`child-command`), `PrimitiveAuthoringFlowTests`
(`nested-command`), `PerStepConditionAuthoringRoundTripIntegrationTests` (`cmd-mail`, `cmd-rewards`),
`SequenceExecuteDryRunIntegrationTests` (`stale-cmd` — the test is *about* a dangling reference, so
it must create the command, create the sequence, then delete the command), and
`WaitForImageSequenceExecutionIntegrationTests` (`cmd-after-wait`). `NestedRequireDispatchIntegrationTests`
and `ParameterContractTests` pass a variable id that may already be a real command — confirmed by
the full-suite run. Any further break found by the full run is corrected the same way.
**Decision**: seed the referenced commands (or create-then-delete for the dangling case); never
loosen an assertion.

## R-006 — Error text

**Decision**: `Command reference '{commandId}' does not exist (used by: {stepId, …}).` — one error per
missing id, ids ordered case-insensitively, step ids distinct, mirroring the existing
`Image reference '{id}' does not exist (used by: …).` message.
**Rationale**: consistent with the sibling rule (UX principle III); names both the id and the steps.

## R-007 — Which steps are checked

**Decision**: steps whose action type is `command` and whose payload carries a non-empty `commandId`,
anywhere in the tree (top level, loop `Body`, if `Body`, `ElseBody`). A command step without a
payload `commandId` already gets the B-003 error and is skipped so the caller does not see a second,
confusing "reference '<stepId>' does not exist" error (without a payload id, `CommandId` defaults to
the step id).
