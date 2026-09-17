# Feature Specification: Publish Sequence Step Nesting Rules

**Feature Branch**: `099-sequence-nesting-rules-docs`  
**Created**: 2026-09-17  
**Status**: Draft  
**Input**: GitHub issue #178 (https://github.com/bbqf/GameBotAI/issues/178) — "nested If steps are rejected at runtime, but the constraint is not in the published schema". Closes #178.

## Background

Saving a sequence whose `If` step contains another `If` step in one of its branches is rejected with
`400 "Branch step '<id>' inside if '<id>' must not itself be an if step."`
The issue accepts the constraint itself as reasonable. The defect is that it is **not discoverable from the published API description**: nothing in the published API document for the sequence step contract says an `If` branch may not contain another `If`, so an author learns the rule only by having a write rejected. The asymmetry with `Loop` bodies (which *may* contain an `If`) is equally undocumented.

The rules the service enforces today on every sequence save and validate:

| Container | Allowed child step types | Rejected child step types |
|-----------|--------------------------|---------------------------|
| Top level of a sequence | Action, Loop, If | Break (only valid inside a loop body) |
| Loop body | Action, If, Break | Loop (no nested loops) |
| If branch (then and else) | Action; Break only when the If itself sits directly inside a Loop body | If (no nested ifs), Loop; Break when the If is not inside a Loop body |

Consequence: the deepest permitted nesting is Loop → If → (Action | Break). If-inside-If and Loop-inside-Loop (or Loop-inside-If) are never permitted.

## Clarifications

### Session 2026-09-17

- Q: Where in the published document should the rules live — on the step shape as a whole, on the body/else-branch fields, or both? → A: Both: the full rule table on the step shape's description, and a short container-specific rule on the loop-body/then-branch field and the else-branch field. (Rationale: authors reading either the shape or a single field find the rule; the issue asks for it "on the sequence step contract".)
- Q: Which published shapes must carry the rules (FR-006)? → A: Every named step shape in the published document that exposes a loop body or if branches; response payloads the document publishes without a named step shape have nothing to annotate and are out of scope. (Rationale: descriptions can only attach to shapes the document actually publishes; changing response typing would exceed the issue.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover the nesting rules before writing a sequence (Priority: P1)

A sequence author (human or automation client) reads the published API document to learn how to structure a sequence. On the sequence step contract they find, stated plainly, which step types may appear inside an If branch and inside a Loop body, and that an If may not be nested inside another If — so they design the sequence correctly the first time instead of discovering the rule through a rejected save.

**Why this priority**: This is the whole of the reported defect; it cost the reporter an authoring iteration.

**Independent Test**: Fetch the published API document and read the sequence step contract; the nesting rules are present and match the table in Background.

**Acceptance Scenarios**:

1. **Given** the service is running, **When** a client fetches the published API document, **Then** the sequence step contract's description states the allowed child step types for an If branch (then and else) and for a Loop body.
2. **Given** the published API document, **When** an author reads the description of the If branch fields, **Then** it explicitly says an If step inside an If branch is not permitted.
3. **Given** the published API document, **When** an author reads the description of the Loop body field, **Then** it says a Loop body may contain If and Break steps but not another Loop.
4. **Given** the published API document, **When** an author reads about Break steps in an If branch, **Then** it says Break is only allowed there when the If itself sits inside a Loop body.

---

### User Story 2 - The published rules stay in step with enforcement (Priority: P2)

A maintainer changing the sequence contract or its documentation is warned by an automated test if the nesting-rule text disappears from the published API document.

**Why this priority**: Prevents silent regression of the documentation; secondary to publishing it.

**Independent Test**: Run the automated test suite; a test fails if the nesting-rule statements are missing from the published API document.

**Acceptance Scenarios**:

1. **Given** the nesting-rule descriptions are published, **When** the automated tests run, **Then** a test confirms each rule statement is present on the sequence step contract in the published API document.
2. **Given** the rules are documented, **When** a sequence with an If nested in an If branch is saved, **Then** it is still rejected with the same message as before (behaviour unchanged).

### Edge Cases

- The sequence step shape is referenced from several operations (create, update, patch request bodies) and may be published under more than one name: the rules must be visible under every name the step shape that carries a loop body or if branches is published as, so an author reading any of them finds them.
- The same field holds a Loop's body and an If's then branch: its description must state both containers' rules without ambiguity.
- The else branch is optional: its description must still state the same nesting rules as the then branch.
- Break at the top level of a sequence (outside any loop) is rejected today; the description should mention that Break is only valid inside a Loop body (directly or via an If inside it).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The published API document MUST describe, on the sequence step contract, which step types are allowed inside a Loop body (Action, If, Break) and that a Loop step is not allowed there.
- **FR-002**: The published API document MUST describe, on the If branch fields (then and else), that only Action steps and — when the If sits inside a Loop body — Break steps are allowed, and that If steps and Loop steps are not allowed there.
- **FR-003**: The published description MUST explicitly state that nesting an If step inside another If step's branch is not permitted.
- **FR-004**: The published description MUST state that a Break step is only valid inside a Loop body (directly, or inside an If branch whose If sits in a Loop body).
- **FR-005**: The documented rules MUST match the rules actually enforced by the sequence validation at the time of change (re-verified against the enforcement logic, not copied from the issue).
- **FR-006**: The rules MUST be visible on every named step shape in the published API document that exposes a loop body or if branches: the full rule set on the shape's description, and the container-specific rule on its loop-body/then-branch field and its else-branch field. Payloads published without a named step shape are out of scope.
- **FR-007**: An automated test MUST fail if the nesting-rule statements are removed from the published API document.
- **FR-008**: Sequence validation behaviour, its error messages, and runtime execution MUST NOT change.

### Key Entities

- **Sequence step**: a unit of a sequence; has a step type (Action, Loop, If, Break). A Loop step owns a body list of steps; an If step owns a then branch and an optional else branch.
- **Published API document**: the machine-readable API description the service serves to clients; the place authors look for the contract.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can determine every nesting restriction (all rows of the Background table) from the published API document alone, with zero rejected saves needed to learn them.
- **SC-002**: 100% of published step shapes that expose a loop body or if branches carry the nesting-rule description.
- **SC-003**: Removing the nesting-rule description causes at least one automated test to fail.
- **SC-004**: All existing sequence validation tests pass unchanged (no behavioural change).

## Assumptions

- The published API document is the service's generated OpenAPI/Swagger document; the service does not feed code comments into it, so descriptions are added the same way other recent contract documentation was published.
- Documenting the rules in the property/schema descriptions satisfies "discoverable from the published schema"; expressing them as machine-enforced schema constraints (e.g. per-branch discriminator restrictions) is not required.

## Out of Scope

- Relaxing or changing any nesting restriction (If-inside-If stays rejected).
- Changing validation error messages or runtime behaviour.
- Web UI changes.
