# Feature Specification: Sequence & Command Parameters

**Feature Branch**: `078-sequence-parameters`
**Created**: 2026-08-24
**Status**: Draft
**Input**: User description: "I need to be able to parametrize the commands from the sequences and sequences from the queue templates. Example: if I want to ensure the game is running, I need to specify the emulator, however if I have 3 instances, the only difference is the port number, so I don't need 3 different commands and 3 different sequences, I just have to specify the parameter 3 ports in 3 different templates and these will be propagated via one sequence to one command. Analyze how to implement it in the most efficient way, as there will be many commands that will need this kind of parametrization. Consider also the user-friendliness of the implementation in the UI, migration effort is less of a priority, make sure a migration path is available, but don't build any code for automatic migration, rather provide a clear path how to convert the commands and sequences to being parametrized in the UI and let the user do it manually. Ask questions before taking decisions that influence the behaviour."

## Overview

Today an operator who automates three emulator instances that differ only by their ADB serial must
maintain three near-identical commands and three near-identical sequences. Every authoring change
has to be repeated N times, and drift between the copies is a routine source of silent failures.

This feature makes commands and sequences **reusable across instances** by letting a value that
varies per caller be expressed as a named parameter instead of a hard-coded literal. A single
"ensure game running" command, invoked by a single sequence, can then serve every instance, with the
differing value supplied by whichever queue or queue-template entry drives the run.

## Clarifications

### Session 2026-08-24

- Q: How should parameters be declared and propagated down the Queue Template → Sequence → Command
  chain? → **A: Hybrid (declared + ambient).** Commands and sequences declare a typed parameter
  list; call sites may bind values explicitly; anything unbound falls through by name from the
  enclosing scope. *Rationale: explicit declarations make the UI able to show what a sequence needs
  and enable pre-run validation, while ambient fall-through avoids re-mapping the same name (e.g.
  `adbSerial`) at every nesting level — essential because many commands will need the same value.*
- Q: Where do the actual values live for the 3-emulator case? → **A: Built-in queue variables plus
  per-template-entry overrides.** *Rationale: the queue already stores the emulator serial, instance
  name/index and linked game, so exposing them as read-only built-ins makes the motivating scenario
  work with zero new configuration; per-entry overrides then cover the cases the queue cannot
  express.*
- Q: What happens when a placeholder cannot be resolved at execution time? → **A: Fail fast, plus
  pre-run validation.** *Rationale: this system taps real device screens; substituting empty text or
  passing a literal placeholder through to a device action risks acting on the wrong instance or
  producing unintelligible downstream errors.*
- Q: Which fields accept parameters? → **A: All string and numeric leaf fields** of sequence action
  payloads and command step configuration; command and sequence references stay non-substitutable.
  *Rationale: broad coverage is needed because "many commands" will be parametrized, while excluding
  references preserves the existing static dangling-reference validation.*
- Q: May a queue-template entry supply names the referenced sequence does not declare, so they flow
  ambiently to nested commands? → **A: Yes — declared bindings plus free ad-hoc values.**
  *Rationale: this is what makes the motivating case ceremony-free — the entry sets `adbSerial`, the
  leaf command declares and consumes it, and the intermediate sequence declares nothing; the cost is
  that a mistyped ad-hoc name can only be reported as an unconsumed-value warning.*
- Q (auto-resolved): Are parameter names case-sensitive? → **A: Yes**, matching the existing
  loop-iteration placeholder; declarations differing only by case are rejected as duplicates.
  *Rationale: a single matching rule avoids two names resolving to one value in one scope and two in
  another.*
- Q (auto-resolved): How is static validation of image/detection references affected when the
  reference is parametrized? → **A: Existence checking is skipped for that field and a warning is
  shown**; the reference is validated at run time instead. *Rationale: the target is unknown until
  resolution, and silently passing an unknown reference would defeat FR-017.*
- Q (auto-resolved): Are resolved parameter values redacted in execution logs? → **A: No, they are
  logged in full.** *Rationale: the values are device serials, instance names and timings — no
  credentials are in scope — and diagnosability is a stated success criterion.*

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One sequence drives every emulator instance (Priority: P1)

An operator runs the same daily automation against three emulator instances. Each instance has its
own queue, and those queues already differ only by the ADB serial they are bound to. The operator
edits the single "ensure game running" command so that the emulator serial field says "use the
queue's emulator" instead of a hard-coded serial, and points all three queues at the same sequence.
Each queue run supplies its own serial automatically.

**Why this priority**: This is the entire motivating problem, and because queues already store the
emulator serial, instance name/index and linked game, it can be delivered without the operator
configuring anything new. Shipping only this story already collapses N duplicated commands and
sequences into one.

**Independent Test**: Author one command whose emulator serial is the built-in queue emulator
variable, reference it from one sequence, attach that sequence to two queues bound to different
serials, run both, and confirm from the execution log that each run targeted its own serial.

**Acceptance Scenarios**:

1. **Given** a command whose ADB serial field contains the built-in queue emulator-serial
   placeholder, **When** it runs inside a queue bound to serial `emulator-5558`, **Then** the device
   action is dispatched against `emulator-5558`.
2. **Given** the same command and sequence, **When** they run inside a second queue bound to serial
   `emulator-5560`, **Then** the device action is dispatched against `emulator-5560` and no edit to
   the command or sequence was required between the two runs.
3. **Given** a command that references the built-in queue linked-game variable, **When** it runs in a
   queue whose linked game is set, **Then** the game-aware action resolves that game.
4. **Given** a command that references the built-in queue instance-index variable in a numeric field,
   **When** it runs in a queue whose instance index is `1`, **Then** the numeric field receives the
   number `1`.
5. **Given** an existing command, sequence and queue template saved before this feature, **When**
   they are loaded and run unchanged, **Then** behaviour is identical to before the feature.

---

### User Story 2 - Declare parameters and bind values per caller (Priority: P2)

An operator needs a value that the queue does not already know — for example a per-entry wait
threshold, an alternate package name, or a screen coordinate that differs per instance. They declare
a named parameter on the command (or sequence), give it a default, and supply the differing value
from the sequence step that invokes the command, or from the queue-template entry that invokes the
sequence.

**Why this priority**: Extends the mechanism beyond the four values a queue happens to store, which
is required for the stated expectation that "many commands" will be parametrized. Depends on the
resolution machinery from US1 but is independently demonstrable.

**Independent Test**: Declare a parameter with a default on a command, invoke it from a sequence
without binding anything (default applies), then bind an explicit value at the sequence step and
confirm the bound value wins.

**Acceptance Scenarios**:

1. **Given** a command declaring parameter `waitMs` with default `5000`, **When** a sequence invokes
   it with no binding and no ambient value of that name exists, **Then** the command runs with
   `5000`.
2. **Given** the same command, **When** the invoking sequence step binds `waitMs` to `9000`,
   **Then** the command runs with `9000`.
3. **Given** a sequence declaring parameter `targetSerial`, **When** two queue-template entries
   reference that sequence and bind `targetSerial` to different values, **Then** each entry's firing
   uses its own value and the two entries do not interfere.
4. **Given** a sequence declaring `adbSerial` that is not bound by the template entry, **When** the
   run scope contains an ambient `adbSerial`, **Then** the ambient value is used without any
   re-mapping.
5. **Given** a template entry that supplies `adbSerial` while its referenced sequence declares no
   parameters at all, **When** a command two levels down declares and references `adbSerial`,
   **Then** the command receives the entry's value and the intermediate sequence needed no edit.
6. **Given** a template entry supplying a name that nothing in its invocation chain declares or
   references, **When** the entry is saved, **Then** a non-blocking warning names the unused value
   and the entry still saves and runs.
7. **Given** a parameter resolvable from several places at once, **When** the step runs, **Then** the
   value is taken in this order of precedence: explicit binding at the call site, then the ambient
   value inherited from the enclosing scope, then the parameter's declared default.
8. **Given** a sequence step inside a loop that also uses the loop iteration placeholder, **When** it
   runs, **Then** both the iteration placeholder and parameter placeholders resolve in the same step.
9. **Given** a sequence that schedules an additional firing of itself, **When** that additional
   firing executes, **Then** it resolves parameters from the same bindings as the originating firing.

---

### User Story 3 - Author parameters without hand-typing placeholders (Priority: P3)

An operator working in the web UI declares parameters in a dedicated section of the command and
sequence editors, and inserts references to them by picking from a list of names that are in scope
rather than typing brace syntax. Where a sequence invokes a command, or a template entry invokes a
sequence, the editor shows the callee's declared parameters as a small form with the effective value
and its origin previewed. Problems are shown while editing, not only when a run fails at 03:00.

**Why this priority**: The mechanism is only useful if it is faster than duplicating entities.
Discoverability of in-scope names and pre-run validation are what make it so, but the underlying
behaviour (US1/US2) is testable and valuable without the conveniences.

**Independent Test**: In the UI, declare a parameter, insert it into a field via the picker without
typing braces, save, and observe the binding form and effective-value preview on the invoking step
and template entry.

**Acceptance Scenarios**:

1. **Given** the command editor, **When** the operator opens the Parameters section, **Then** they
   can add, edit, reorder and remove parameter declarations with name, type, default, required flag
   and description.
2. **Given** a parametrizable field in an editor, **When** the operator invokes the insert-parameter
   affordance, **Then** a list of in-scope names is offered — the entity's own declared parameters
   and the queue built-ins — each with its description, and choosing one inserts a valid reference.
3. **Given** a sequence step that invokes a command with declared parameters, **When** the step is
   opened, **Then** each declared parameter is shown pre-set to "inherit", so the common case
   requires no interaction.
4. **Given** a queue-template entry referencing a sequence with declared parameters, **When** the
   entry is opened, **Then** each parameter shows its effective value and which scope produced it
   (entry override, queue built-in, or declared default).
5. **Given** a field referencing a name that nothing in scope can supply, **When** the operator saves
   the command or sequence, **Then** an inline validation error identifies the field and the
   unresolvable name and the save is rejected.
6. **Given** a queue template whose entries leave required parameters unsupplied, **When** the
   operator starts the queue, **Then** the start is refused with a message naming each entry and
   each missing parameter.
7. **Given** a queue-template list, **When** entries carry parameter overrides, **Then** those
   entries are visibly marked as overridden without opening them.
8. **Given** a sequence run started ad hoc from the UI (outside any queue), **When** the sequence
   declares parameters, **Then** the operator is shown a value form pre-filled with defaults, and
   the run is refused while any required parameter is empty.

---

### User Story 4 - Convert existing duplicates by hand, safely (Priority: P4)

An operator with several sets of duplicated commands and sequences follows a written procedure to
collapse each set into one parametrized pair, verify the result against a real instance, and then
delete the redundant copies.

**Why this priority**: No automatic migration is being built, so the written path is the migration
path. It is last because it depends on the finished behaviour and UI it describes.

**Independent Test**: A reader who has never used the feature can follow the guide end to end on the
ensure-game-running / ADB-serial case and reach a working single-command, single-sequence setup.

**Acceptance Scenarios**:

1. **Given** the shipped conversion guide, **When** an operator follows it for a set of N duplicated
   commands, **Then** they end with one command, one sequence, and N template entries or queues that
   supply the differing value.
2. **Given** the guide, **When** the operator reaches the verification step, **Then** they are told
   exactly what to check in the execution log to confirm each instance was targeted correctly before
   anything is deleted.
3. **Given** the guide, **When** the operator reaches the cleanup step, **Then** they are told how to
   confirm a duplicate command or sequence is no longer referenced before deleting it.

---

### Edge Cases

- **Unresolvable name at run time**: a placeholder that no scope can supply fails the step with an
  error naming the parameter, the field and the step. It is never replaced with empty text and the
  literal placeholder is never passed to a device-level action.
- **Numeric coercion failure**: a value that cannot be interpreted as the target numeric field's type
  (non-numeric text, out of range, fractional where whole numbers are required) fails the step with
  an error naming the parameter, the offending value and the target field.
- **Reserved namespace**: user-declared parameters may not use the reserved built-in namespace, nor
  the reserved loop-iteration name; attempting to declare one is rejected at save time. The same
  restriction applies to ad-hoc values supplied on a template entry.
- **Ad-hoc value shadowing a declaration**: when a template entry supplies an ad-hoc name that a
  deeper command also declares with a default, the supplied value wins, exactly as an inherited
  ambient value outranks a default under FR-009.
- **Unconsumed ad-hoc value**: a supplied name that nothing references is a warning, never an error;
  the entry saves and runs normally.
- **Case mismatch**: a reference whose spelling differs from the declaration only by case does not
  resolve, and is reported as an unresolvable name rather than silently matching.
- **Built-in with no value**: a queue that has no instance name, instance index or linked game set
  makes those built-ins unavailable, so referencing one behaves exactly like an unresolvable name.
- **Run outside a queue**: an ad-hoc or trigger-initiated run has no queue built-ins; required
  parameters must be supplied by the run's own value form or by declared defaults, otherwise the run
  is refused before any device action.
- **Same sequence, two entries**: two template entries referencing the same sequence hold independent
  bindings; changing one must not affect the other, including when both are enabled and scheduled.
- **Declaration removed or renamed**: bindings left pointing at a parameter that no longer exists are
  reported as validation problems on the referencing entity, and do not silently disappear.
- **Empty string as a deliberate value**: an explicitly bound empty value is a real value and
  satisfies resolution; it is distinct from "unbound".
- **Whole-field vs embedded placeholders**: a field may be entirely a placeholder or may embed one
  within surrounding text; both resolve, but numeric fields only accept a whole-field placeholder.
- **Nesting**: a parameter referenced inside a loop body, an if branch, or a nested command invocation
  resolves against the scope in effect at that point, composed with the loop iteration context.
- **Guard sequences**: sequences that run on the every-step schedule resolve parameters from their own
  template entry's bindings, like any other entry.

## Requirements *(mandatory)*

### Functional Requirements

#### Declaration

- **FR-001**: Commands MUST be able to declare an ordered list of parameters, each with a unique
  name, a type of either text or number, an optional default value, a required flag, and an optional
  description.
- **FR-002**: Sequences MUST be able to declare parameters using the same shape and rules as commands.
- **FR-003**: The system MUST reject parameter names that collide with the reserved built-in
  namespace, collide with the reserved loop-iteration name, duplicate another parameter on the same
  entity, or are empty. Names are case-sensitive when referenced, and two declarations on the same
  entity differing only by case MUST be rejected as duplicates.
- **FR-004**: Absence of a parameter declaration list MUST be indistinguishable from today's
  behaviour, so entities saved before this feature continue to load and run unchanged.

#### Reference and substitution

- **FR-005**: Any text-typed leaf field of a sequence action payload or a command step configuration
  MUST accept a parameter reference, either as the whole field value or embedded in surrounding text.
- **FR-006**: Any numeric leaf field of a sequence action payload or a command step configuration
  MUST accept a whole-field parameter reference, whose resolved value is converted to the field's
  numeric type before the step executes.
- **FR-007**: Command references and sequence references MUST NOT be substitutable, so existing
  static validation of dangling references remains exact.
- **FR-008**: Parameter substitution MUST compose with the existing loop-iteration substitution so
  that both kinds of placeholder resolve within the same step.

#### Resolution

- **FR-009**: The system MUST resolve a parameter using this precedence, first match wins: (1) an
  explicit binding supplied at the invoking call site, (2) an ambient value inherited from the
  enclosing run scope, (3) the parameter's declared default.
- **FR-010**: A queue run MUST expose its emulator serial, emulator instance name, emulator instance
  index and linked game identifier as read-only built-in values in the run scope, under a reserved
  namespace, available to every sequence and command executed in that run.
- **FR-011**: A built-in whose underlying queue field is unset MUST be treated as absent from scope,
  not as an empty value.
- **FR-012**: A queue-template entry MUST be able to bind values for the parameters of the sequence
  it references; bindings are per entry, so duplicate entries referencing the same sequence hold
  independent bindings.
- **FR-012a**: A queue-template entry MUST additionally be able to supply named values that the
  referenced sequence does not declare. Such values enter the run scope for that entry's firing and
  are inheritable by any command invoked at any depth beneath it, so an intermediate sequence need
  not re-declare a parameter merely to pass it through.
- **FR-012b**: A supplied value that no command or sequence in the entry's reachable invocation chain
  declares or references MUST be reported as a non-blocking warning on the entry, naming the unused
  value, so that a mistyped name is discoverable without preventing the run.
- **FR-013**: A sequence step that invokes a command MUST be able to bind values for that command's
  parameters, and MUST default to inheriting rather than binding.
- **FR-014**: A parameter that is not explicitly bound at a call site MUST inherit the value of the
  same name from the enclosing scope without requiring any re-mapping at intermediate levels.
- **FR-015**: An additional firing scheduled by a sequence rescheduling itself MUST resolve
  parameters from the same bindings as the firing that scheduled it.
- **FR-016**: Resolution MUST occur immediately before a step is dispatched, so that values reflect
  the scope in effect at that point, including inside loop and conditional bodies.

#### Failure handling and validation

- **FR-017**: When a referenced name cannot be resolved from any scope, the step MUST fail with an
  error identifying the parameter name, the field, and the step.
- **FR-018**: No device action MUST be dispatched for a step whose resolution failed. In particular
  the system MUST NOT substitute an empty value for an unresolved reference, and MUST NOT pass an
  unresolved reference through to any device-level action. (FR-017 governs the reporting; this
  requirement governs the side effect, and the two are verified separately.)
- **FR-019**: When a resolved value cannot be converted to a numeric field's type, the step MUST fail
  with an error identifying the parameter, the value, and the target field.
- **FR-020**: Saving a command or sequence MUST be rejected when it references a name that neither
  its own declarations nor the built-in namespace nor the loop-iteration context can supply, with the
  offending field and name identified.
- **FR-021**: Saving a queue template MUST report any entry whose referenced sequence has required
  parameters that the entry, the built-ins, and the declared defaults together cannot supply.
- **FR-022**: Starting a queue MUST be refused when any enabled entry has required parameters that
  cannot be supplied, with each affected entry and parameter named.
- **FR-023**: Bindings that reference a parameter no longer declared by the callee MUST be surfaced as
  a validation problem on the referencing entity rather than silently ignored.
- **FR-023a**: When a field that normally undergoes static existence checking — such as an image or
  detection reference — is parametrized, that static check MUST be skipped for the field and replaced
  by a non-blocking warning at save time; the resolved value MUST still be validated at run time and
  fail the step under FR-017 when it does not exist.
- **FR-024**: Execution logs MUST record the resolved values used for a step's parameters, unredacted,
  so a run can be diagnosed after the fact.

#### Authoring experience

- **FR-025**: The command editor and the sequence editor MUST each provide a Parameters section for
  declaring, editing, reordering and removing parameters.
- **FR-026**: Every parametrizable field in the editors MUST offer an affordance that lists the names
  currently in scope with their descriptions and inserts a valid reference on selection, so the
  operator never has to type the reference syntax by hand.
- **FR-027**: A sequence step invoking a command MUST display that command's declared parameters as a
  binding form, with every entry pre-set to inherit.
- **FR-028**: A queue-template entry MUST display its referenced sequence's declared parameters as a
  binding form showing, per parameter, the effective value and which scope produced it, and MUST let
  the operator add further name/value pairs beyond the declared list, visually distinguished from
  declared parameters.
- **FR-029**: Validation problems relating to parameters MUST be shown inline in the editor at the
  offending field, in addition to being reported when a run is started.
- **FR-030**: The queue-template entry list MUST visually indicate which entries carry parameter
  overrides without requiring the entry to be opened.
- **FR-031**: A sequence run started outside any queue MUST present a value form pre-filled with
  declared defaults and MUST refuse to start while a required parameter has no value.

#### Compatibility and migration

- **FR-032**: Stored commands, sequences, queues and queue templates that predate this feature MUST
  load and behave exactly as before, with absent declarations and absent bindings meaning today's
  semantics.
- **FR-033**: No automatic migration or data-upgrade routine MUST be built; conversion of existing
  duplicates is a manual, operator-driven activity.
- **FR-034**: The feature MUST ship a written conversion guide that walks an operator through
  collapsing N duplicated commands and sequences into one parametrized pair through the UI, using the
  ensure-game-running / ADB-serial case as the worked example, including how to verify the converted
  setup against each instance and how to confirm a duplicate is unreferenced before deleting it.

### Key Entities

- **Parameter Declaration**: A named, typed input that a command or a sequence accepts. Attributes:
  name (unique within its owner), type (text or number), optional default value, required flag,
  description. Owned by exactly one command or one sequence; ordered for display.
- **Parameter Binding**: A value supplied at a specific call site. Attributes: name, supplied value,
  or the explicit "inherit" state. Held by a queue-template entry (binding a sequence's parameters,
  and optionally supplying further ad-hoc names per FR-012a) or by a sequence's command step (binding
  a command's parameters). A binding whose name matches a declaration is a *declared* binding; one
  that does not is an *ad-hoc* value, valid only on a queue-template entry.
- **Run Scope**: The set of names visible to a step at the moment it is dispatched. Composed of the
  queue built-ins for the run, the bindings and defaults accumulated down the invocation chain, and
  the loop iteration context. Not persisted; exists only for the duration of a firing.
- **Queue Built-in Values**: Read-only names derived from the executing queue's existing
  configuration — emulator serial, emulator instance name, emulator instance index, linked game
  identifier — exposed under a reserved namespace and never user-declarable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Automating N emulator instances that differ only by their ADB serial requires exactly
  one command and one sequence, regardless of N.
- **SC-002**: For the motivating case — instances differing only by a value the queue already stores
  — an operator can convert an existing duplicated setup without creating any new parameter
  declaration or binding.
- **SC-003**: Changing shared automation logic once applies to every instance, so the number of edits
  needed to fix a shared step drops from N to 1.
- **SC-004**: An operator can insert a parameter reference into any parametrizable field without
  typing reference syntax, in at most three interactions from the field.
- **SC-005**: 100% of runs that would otherwise dispatch a device action with an unresolved or
  unconvertible parameter instead fail with an error that names the parameter, the field and the step.
- **SC-006**: 100% of missing required-parameter situations that are statically detectable are
  reported before a run starts rather than during it.
- **SC-007**: Every command, sequence, queue and queue template stored before this feature loads and
  runs with unchanged behaviour, with no operator action required.
- **SC-008**: An operator who has not used the feature before can convert one duplicated set of three
  commands and three sequences into a single parametrized pair, verified against all three instances,
  in under 20 minutes using only the shipped guide.
- **SC-009**: After a run, an operator can determine from the execution log which value each
  parametrized step actually used.
- **SC-010**: Passing a value from a queue-template entry to a command nested at any depth requires
  edits to exactly two entities — the entry that supplies it and the command that consumes it —
  regardless of how many sequences sit between them.

## Assumptions

- The existing placeholder syntax already used for the loop iteration value is extended for
  parameters rather than a second, competing syntax being introduced.
- Parameter types are limited to text and number. Boolean and structured types are out of scope; a
  boolean-valued setting stays a fixed field for now.
- Parameter values are configuration, not run history: they live on queues and queue templates and
  are not persisted per firing beyond the execution log record required by FR-024.
- The reserved built-in namespace covers only the four queue-derived values listed in FR-010;
  additional built-ins can be added later without changing the resolution rules.
- Renaming a parameter is an edit like any other: it surfaces stale bindings as validation problems
  (FR-023) but does not attempt to rewrite referencing entities.
- Ad-hoc values (FR-012a) are permitted only on queue-template entries, which are the outermost call
  site. A sequence's command step binds only names the command declares, so a typo there stays a
  hard validation error rather than becoming a silently-unused value.
- Parameter names are matched case-sensitively everywhere: declarations, references, bindings and
  built-ins.
- Queue templates remain the durable place where entry-level configuration lives, consistent with
  today's behaviour where a queue run is built from its linked template.

## Out of Scope

- Any automatic migration, backfill, or data-upgrade tooling.
- Expressions, arithmetic, conditionals, or string functions inside placeholders — a reference
  resolves to a value and nothing more.
- Parameters that select which command or sequence executes.
- Persisting parameter values across runs beyond the queue and queue-template configuration.
- Parameter types other than text and number.
- Sharing or importing parameter sets between templates as a first-class reusable object.
