# Feature Specification: Composite Image Conditions

**Feature Branch**: `088-composite-image-conditions`
**Created**: 2026-09-15
**Status**: Implemented
**Input**: GitHub issue [#191](https://github.com/bbqf/GameBotAI/issues/191) — "B-011: a condition cannot require two templates together, so one button shared by two dialogs is undisambiguable"

## Problem Context

A sequence step can be guarded by a condition, and today a condition is a **single** test: one reference image is visible, or one earlier step produced a given outcome. That single test is not always enough to identify the screen the automation is looking at.

The reported case: two unrelated game dialogs draw an identical `Confirm` button at identical coordinates. A reference image cropped from the first dialog matches the second dialog at score **1.0** — an exact match, not a marginal one. Negative controls on unrelated screens score 0.31–0.49, so the image matcher is behaving correctly; it reports exactly what is on screen. What is missing is the ability to say *"this button, **and** that dialog's title"* or *"this button, but **not** when that other dialog's title is present"*.

The distinguishing pixels (the dialog's own text) sit far from the button and cannot be folded into a single crop rectangle without dragging in half the modal, so no re-crop and **no threshold change can fix it** — the match is exact.

The consequence is a real safety failure, not a cosmetic one. A dismiss action guarded only by the ambiguous button taps whatever dialog is actually showing. In the reported case that is a "get more of a paid resource now?" prompt, which the project constitution forbids the automation from being able to reach. A false positive, not a mis-aimed tap, defeats the guard.

## Clarifications

### Session 2026-09-15

Answered autonomously during an unattended pipeline run. Each answer records the reasoning that selected it.

- Q: What vocabulary should the combining rules use — `all`/`any`/`none`, or `and`/`or`/`not`? → A: `all` / `any` / `none`.
  *Why*: this is the vocabulary the source issue asks for, and it fits a rule that owns an ordered **list** of children better than binary operators do. `none` also states "and not that dialog" directly, which is the idiom the report needs, instead of making an author wrap an `any` in a negation. The project already carries a separate `and`/`or`/`not` expression tree for a different step type; reusing those names here would imply the two models are the same thing when they are not.
- Q: What are the concrete bounds on composite size? → A: nesting depth at most 4, at most 16 children per composite.
  *Why*: real guards combine two or three signals, and the deepest sensible shape ("A and B and not (C or D)") is depth 3. Depth 4 leaves headroom while keeping worst-case evaluation and validation recursion trivially bounded; 16 children is far past any hand-written guard and still caps the per-guard screen-evaluation cost.
- Q: Is a composite with exactly one child valid? → A: Yes; only an empty child list is rejected.
  *Why*: a one-child composite is a harmless identity, and rejecting it would force any tool that builds conditions programmatically to special-case the single-element result. This is a deliberate difference from the older flow-condition expression tree, which demands at least two children for its `and`/`or` nodes; that rule exists there because those nodes are hand-authored, and it is not worth propagating.
- Q: Do the children of one composite judge the same screen, or may each re-observe it? → A: The same screen observation serves the whole composite evaluation.
  *Why*: a guard that saw image A on one frame and checked image B on a later frame could act on a screen that never existed, which is exactly the class of bug this feature exists to remove. Sharing one observation also means a two-signal guard costs no more screen captures than today's one-signal guard.
- Q: How is a skip caused by a composite reported? → A: Through the existing per-step execution record, with the composite's kind in the condition-type field and the deciding child named in the message. No new log structure.
  *Why*: the existing record already carries a condition type, a condition result and a message, and downstream consumers read that shape. Naming which child settled the outcome is what makes a false guard diagnosable, and it fits in the message field without changing the record's contract.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Require two signals together (Priority: P1)

An automation author writes a step that should run only when a specific dialog is on screen, identified by two reference images that are individually ambiguous but together unique: the shared button **and** the dialog's own title.

**Why this priority**: This is the reported defect and the whole point of the feature. Without it, an author has no way at all to disambiguate a shared control, and safety guards built on a single template can fire on the wrong dialog. Everything else in this spec is refinement around this capability.

**Independent Test**: Create a sequence whose step condition requires both images, run it against a screen showing only the first image, and confirm the step does not act; run it against a screen showing both and confirm it does.

**Acceptance Scenarios**:

1. **Given** a step whose condition requires image A **and** image B, **When** the screen shows A but not B, **Then** the condition evaluates false and the step does not execute its action.
2. **Given** the same step, **When** the screen shows both A and B, **Then** the condition evaluates true and the step executes.
3. **Given** the same step, **When** the screen shows neither image, **Then** the condition evaluates false.

---

### User Story 2 - Exclude a known look-alike (Priority: P1)

An automation author writes a step that should run when a control is visible **except** when a specific other dialog is also on screen — the exclusion form of the same problem, and the form that directly prevents the reported unsafe tap.

**Why this priority**: Equal in importance to Story 1, and the safer of the two idioms when the wanted dialog has no unique anchor of its own but the unwanted one does. This is the form that closes the constitution violation in the report.

**Independent Test**: Create a step conditioned on "A visible and B not visible", show a screen carrying both, and confirm the step is skipped.

**Acceptance Scenarios**:

1. **Given** a step whose condition requires image A and requires that image B is **not** present, **When** the screen shows both A and B, **Then** the condition evaluates false and the step is skipped.
2. **Given** the same step, **When** the screen shows A alone, **Then** the condition evaluates true.

---

### User Story 3 - Collapse a fan-out of alternatives into one condition (Priority: P2)

An automation author who today writes several separate single-image exit guards — one per look-alike screen, OR'd by repetition — replaces them with one condition listing the alternatives.

**Why this priority**: A real but secondary benefit. The issue calls it out as a bonus ("would also remove the need for the N-OR'd-`Break` idiom used elsewhere"). It simplifies existing automation but nothing is currently broken by its absence.

**Independent Test**: Replace a group of repeated single-image exit guards with one alternatives condition and confirm the loop exits on the same screens as before.

**Acceptance Scenarios**:

1. **Given** a loop exit condition listing images A, B and C as alternatives, **When** any one of them is on screen, **Then** the loop exits.
2. **Given** the same condition, **When** none of them is on screen, **Then** the loop continues.

---

### User Story 4 - Existing automation keeps working untouched (Priority: P1)

Every sequence already stored, and every sequence written against the old single-test shape, continues to load, validate, evaluate and save exactly as before.

**Why this priority**: There are live production sequences running on a schedule. A change that silently alters how an existing condition evaluates, or that fails to load a stored sequence, is worse than the defect being fixed.

**Independent Test**: Load, read back, edit and re-save a pre-existing sequence that uses single conditions, and confirm both its stored form and its runtime behaviour are unchanged.

**Acceptance Scenarios**:

1. **Given** a sequence stored before this change, **When** it is loaded and executed, **Then** its conditions evaluate exactly as they did before.
2. **Given** that sequence, **When** it is read and saved back without edits, **Then** its stored condition shape is unchanged.

---

### Edge Cases

- **Empty list of children**: a composite that combines nothing is meaningless and its truth value would be an arbitrary convention. It is rejected when the sequence is saved, rather than quietly evaluating true or false at runtime.
- **A single child**: permitted. A composite wrapping one condition is redundant but harmless and evaluates as that child does.
- **Deep nesting**: a composite may contain composites, to a maximum depth of 4. A payload exceeding that is rejected at save time with a message naming the limit, so a hostile or accidentally recursive payload cannot exhaust resources.
- **Too many children**: a single composite holds at most 16 children, for the same reason, since every child may cost a screen evaluation.
- **Unknown or malformed composite payload**: rejected with a client error explaining what is wrong, never an unhandled server error.
- **A child that is itself invalid** (for example an image condition naming an image that does not exist): reported with enough location information to identify which child of which step is at fault.
- **Cost of evaluation**: a composite that has already decided its outcome stops evaluating the remaining children, so an unnecessary screen match is never performed.
- **Negation of a composite**: the existing per-condition negation flag applies to a composite as a whole, and the result is the plain logical inverse of what the composite evaluated to.
- **A composite used as a loop exit guard**: behaves identically to a composite used as a step guard; no condition position is special.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The condition model MUST support a composite condition that is true only when **all** of its child conditions are true.
- **FR-002**: The condition model MUST support a composite condition that is true only when **none** of its child conditions is true.
- **FR-003**: The condition model MUST support a composite condition that is true when **any** of its child conditions is true, so that a fan-out of alternative guards collapses into one condition.
- **FR-004**: A composite condition MUST accept any supported condition kind as a child, including image-visibility conditions, prior-step-outcome conditions, and other composites.
- **FR-005**: Composite conditions MUST be accepted in **every** position that accepts a condition today, and there are five: a step's guard, a loop's break guard, a branch test, a while-loop condition, and a repeat-until-loop condition. Acceptance means parsed, validated, stored, evaluated and returned — not merely tolerated by the parser.
- **FR-006**: The existing per-condition negation flag MUST continue to work on single conditions and MUST apply to a composite as a whole, inverting its combined result.
- **FR-007**: The system MUST reject a condition nested more than **4 levels** deep, with a message that names the limit and the offending location.
- **FR-008**: The system MUST reject a composite holding more than **16 children**, with a message that names the limit and the offending location.
- **FR-009**: The system MUST reject a composite with an empty child list at save time, rather than assigning it a default truth value at runtime. A composite holding exactly one child MUST be accepted and MUST evaluate as that child does (subject to the composite's own combining rule and negation flag).
- **FR-010**: Every rejection of an invalid condition payload — malformed, empty, too deep, too wide, or referencing something that does not exist — MUST be reported as a client error with an actionable message, never as an unhandled server error.
- **FR-011**: Condition evaluation MUST short-circuit: an "all" stops at its first false child, an "any" at its first true child, and a "none" at its first true child, so no further child is evaluated once the outcome is settled.
- **FR-012**: Children MUST be evaluated in the order the author wrote them, so that an author can place the cheapest or most selective test first and rely on it being tried first.
- **FR-013**: Conditions MUST round-trip without loss through create, read and update, including nested composites and every child's own settings.
- **FR-014**: The published API description MUST document the composite condition kinds, their discriminator values, their recursive child list, and the depth and width limits, so the capability and its constraints are discoverable without reading source or trial-and-error.
- **FR-015**: Sequences written against the existing single-condition shape MUST continue to load, validate, evaluate and save with unchanged behaviour and unchanged stored form.
- **FR-016**: When a composite condition decides whether a step ran, the recorded execution outcome MUST identify the composite's combining rule and MUST name the child that settled the result, reusing the existing per-step execution record rather than introducing a new one.
- **FR-017**: All children of one composite MUST be judged against the same screen observation, so a composite can never report a combination that was never simultaneously true.

### Key Entities

- **Condition**: the guard attached to a step, a loop exit, or a branch. Previously always a leaf test; now either a leaf test or a composite. Carries a negation flag in both forms.
- **Leaf condition**: an existing single test — an image being visible on screen (with an optional match threshold), or an earlier step having reached a given outcome.
- **Composite condition**: a condition holding an ordered, non-empty, bounded list of child conditions and a combining rule (all / any / none). A child may itself be a composite, up to a bounded depth.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An automation author can express "act only when this control and that dialog title are both on screen" in a single guard, with no repetition of the step and no reliance on match thresholds.
- **SC-002**: In the reported scenario — one reference image that matches two different dialogs at full confidence — the guarded action fires on the intended dialog and does **not** fire on the look-alike, demonstrated end to end.
- **SC-003**: 100% of sequences that exist before the change load, evaluate and save identically after it, with no edit required.
- **SC-004**: Every malformed composite payload that a reasonable author could produce — empty, over-deep, over-wide, unknown kind, bad child reference — returns a client error naming the problem; none returns a server error.
- **SC-005**: A guard needing N alternative screens is expressed as one condition rather than N repeated guards, reducing that idiom's authoring size by roughly a factor of N.
- **SC-006**: A composite performs no more screen evaluations than the number of children needed to settle its outcome, so adding a second signal to a guard costs at most one extra evaluation and often none.
- **SC-007**: A reader of the published API description can construct a valid nested composite condition correctly on the first attempt, without consulting source code.

## Assumptions

- **A-001**: The three combining rules **all**, **any** and **none** are sufficient. Arbitrary boolean expressions (mixed precedence, explicit parentheses, an expression language) are not needed, because any such expression is expressible by nesting these three.
- **A-002**: `none` is retained as a distinct rule even though it equals a negated `any`, because "and not that dialog" is the idiom the report asks for and spelling it directly is clearer than asking authors to compose a negation.
- **A-003**: The depth-4 / 16-child limits are a safety bound rather than a design constraint on real automation; real guards combine two or three signals, so the limits are invisible in practice (see Clarifications).
- **A-004**: Children are evaluated sequentially, not in parallel. The count is tiny, ordering is author-visible and useful, and short-circuiting is worth more than concurrency here.
- **A-005**: Sharing one screen observation across a composite's children (FR-017) is achievable with the observation the runtime already makes for a single image test, so a two-signal guard adds no screen capture.
- **A-006**: No change to the image matcher, to its scoring, or to the detection endpoints. The report is explicit that the matcher is correct.
- **A-007**: Authoring UI work is out of scope; the capability is delivered at the API level, which is how the affected automation is authored today.
- **A-008**: The product already contains a separate boolean expression tree used by a different, block-style flow step. It is **not** the model this feature extends, and this feature does not merge, replace or deprecate it. The two remain distinct; only the per-step / loop-exit / branch guard gains composites. Converging them is possible later but would change behaviour for existing flow steps and is deliberately excluded here.

## Out of Scope

- Any change to image matching, scoring, or thresholds.
- Any change to the behaviour or response shape of the image detection endpoints (tracked separately as issue #188).
- Reference-image masking and multi-crop templates (tracked separately as issues #190 and #192).
- Region-scoped or coordinate-relative constraints between images ("A above B", "B within 40px of A").
- Changes to the game-automation sequence data held in the external automation repository; adopting the new condition kind there is follow-on work.
- New authoring UI for building composite conditions, beyond whatever is needed to keep the existing UI building.
