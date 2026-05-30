# Research: Preserve Sequence Step Command Names

## Decision 1: Canonical authoring round-trip shape

- Decision: Treat the per-step object array (`SequenceLinearStep[]`) as the canonical authoring payload for create/get/update flows, and keep legacy `string[]` handling only as compatibility fallback for older callers.
- Rationale: The existing frontend already saves and loads rich step objects with `stepId`, `label`, and primitive/action payloads; reducing them to command-id strings drops the data required to reopen steps accurately.
- Alternatives considered:
  - Continue normalizing authoring saves to `string[]` command ids (rejected: loses step identity and non-command step metadata).
  - Remove all legacy support immediately (rejected: unnecessary risk to older stored content or auxiliary callers).

## Decision 2: Persist user-visible command identity with the step

- Decision: Persist a command-name snapshot alongside the step's command reference when the step targets a command.
- Rationale: The UI cannot show a meaningful unresolved state for deleted or missing commands if it only stores the command id.
- Alternatives considered:
  - Resolve command names live only from the command repository (rejected: deleted commands would still appear blank or unknown).
  - Reuse the step label as the command name source (rejected: step labels and command names serve different purposes and can diverge).

## Decision 3: Unresolved command-reference UX

- Decision: When a saved command id no longer resolves, keep the step intact and expose an explicit unresolved state with the last saved command name when available.
- Rationale: This matches the clarified spec, distinguishes broken references from intentionally blank steps, and avoids destructive data loss.
- Alternatives considered:
  - Show the normal empty `Select command` state (rejected: misleading because it hides that data was previously assigned).
  - Auto-clear the command reference on load (rejected: corrupts historical authoring data).

## Decision 4: Execution-log step wording

- Decision: Sequence execution-log step entries include both the step label and the selected command name for command-backed steps.
- Rationale: Operators need a stable authoring-facing step identity and the human-recognizable command that actually ran.
- Alternatives considered:
  - Log only the step label (rejected: still ambiguous when step labels are generic or generated).
  - Log command id instead of command name (rejected: less readable for operators and unnecessary for the clarified requirement).

## Decision 5: Test strategy for the bug fix

- Decision: Cover the fix with one contract/integration round-trip assertion for saved step metadata, one frontend reload assertion for selected command restoration and unresolved state, and one execution-log projection/integration assertion for step label plus command name.
- Rationale: The bug spans serialization, UI mapping, and log projection; one layer alone cannot prove the behavior end-to-end.
- Alternatives considered:
  - UI-only regression coverage (rejected: would miss backend persistence shape regressions).
  - API-only regression coverage (rejected: would not prove the user-facing editor state or execution-log display wording).