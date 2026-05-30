# Feature Specification: Persisted Execution Log

**Feature Branch**: `[028-execution-log]`  
**Created**: 2026-02-27  
**Status**: Draft  
**Input**: User description: "execution log. I want to have a persisted execution log of each command and sequence execution in order to be able to check what failed and what succeeded. The log must contain the following information: Timestamp, Object being executed (also the hierarchy, i.e. if a command was a standalone or part of sequence) and status: success/failure. Object should be identifiable, so that the end user can find what execution object actually failed or succeeded, maybe a link to open it in the authoring UI (watch out for hierarchy) would be helpful. The link should be relative, of course, as we cannot bind to a specific host/port, make a suggestion, how to achieve this. If an object is parametrized (like a primitive tap), make sure you log, what actually happened, e.g. Detected an image ABC at location (x,y) with Confidence a, then Tap at location (x,y). Also necessary information available now in traces should be noted, for example if command execution was not performed due to detection not being able to find anything above threshold. The log should be consise and informative and it should be oriented on the end users, not the developers. Later I'd like to have it displayed in the web-ui, but for now it's enough if it's stored on the backend."

## Clarifications

### Session 2026-02-27

- Q: How should steps that were intentionally not executed (for example detection below threshold) be represented in status semantics? → A: Keep final status as `success`/`failure` and add a separate step outcome flag (`executed`/`not_executed`).
- Q: What log retention period should be used? → A: Keep period configurable.
- Q: For nested executions, should entries carry child-only link, parent-only link, or both? → A: Include both child link and parent hierarchy context in the same entry.
- Q: How should sensitive parameter values be handled in execution logs? → A: Log user-facing details with sensitive values masked or redacted.

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Review execution outcomes (Priority: P1)

As an end user, I can review persisted execution log entries for commands and sequences so I can quickly see what succeeded, what failed, and when.

**Why this priority**: This is the core outcome: users need a durable execution history to diagnose failures after runs complete.

**Independent Test**: Can be fully tested by running one successful command and one failing command, then verifying persisted entries contain timestamp, execution object, hierarchy context, and final status.

**Acceptance Scenarios**:

1. **Given** a command execution completes successfully, **When** the log is persisted, **Then** an entry records execution time, object identity, hierarchy context, and status `success`.
2. **Given** a sequence execution fails, **When** the log is persisted, **Then** the sequence and impacted command entries record failure status and user-readable failure reason.

---

### User Story 2 - Trace sequence hierarchy (Priority: P2)

As an end user, I can see whether a command executed standalone or as part of a sequence, including parent-child relationship details, so I can understand where a failure happened.

**Why this priority**: Hierarchy context is necessary to troubleshoot sequence runs where multiple commands execute in order.

**Independent Test**: Can be tested by running the same command once standalone and once inside a sequence, then validating logs clearly differentiate both contexts.

**Acceptance Scenarios**:

1. **Given** a command runs inside a sequence, **When** its log entry is written, **Then** the entry includes parent sequence identity and relative position in hierarchy.
2. **Given** a command runs standalone, **When** its log entry is written, **Then** the entry indicates no parent sequence and remains uniquely identifiable.

---

### User Story 3 - Understand what actually happened (Priority: P3)

As an end user, I can read concise, outcome-focused execution details for parameterized actions and skipped operations so I know why a step succeeded, failed, or did not execute.

**Why this priority**: Clear, concise activity details reduce support burden and help users self-diagnose without developer-only traces.

**Independent Test**: Can be tested by executing a parameterized action with successful detection and another with detection below threshold, then confirming both outcomes are captured in user-friendly detail.

**Acceptance Scenarios**:

1. **Given** detection succeeds and a tap executes, **When** logging occurs, **Then** the log includes concise details such as detected reference, detected location, confidence value, and executed tap location.
2. **Given** detection does not meet threshold, **When** the command is skipped or fails, **Then** the log includes that execution did not proceed and explains the threshold condition in user-facing language.

---

### Edge Cases

- Multiple executions occur in the same second; each entry still remains uniquely identifiable and ordered.
- A sequence starts but is interrupted mid-run; completed and non-completed child items are distinguishable.
- The referenced object (command/sequence) is later renamed or deleted; historical log entries remain understandable and still reference the original identity captured at execution time.
- An execution detail payload is unusually long; log output remains concise and truncates or summarizes non-essential detail while retaining critical outcome data.
- Relative authoring path cannot be resolved for a historical object; entry keeps object identifiers and marks navigation path as unavailable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST persist an execution log entry for every command execution attempt and every sequence execution attempt.
- **FR-002**: Each execution log entry MUST include execution timestamp, final execution status (`success` or `failure`), and object identity sufficient for end users to determine exactly what was executed.
- **FR-003**: Each execution log entry MUST indicate hierarchy context, including whether execution was standalone or part of a sequence, and when nested MUST identify parent sequence context.
- **FR-004**: Log entries for parameterized or multi-step actions MUST include concise, user-facing outcome details describing what occurred (for example detection result, confidence, resolved coordinates, and follow-up action taken).
- **FR-005**: When execution does not proceed as expected (for example detection below threshold), the log MUST record that execution step as not performed and include the reason in end-user language.
- **FR-006**: Each log entry MUST include a stable, relative authoring navigation path suggestion so a future web UI can open the directly executed object without requiring host or port binding.
- **FR-007**: For hierarchical executions, the system MUST preserve parent-child execution relationships and execution order so users can reconstruct the sequence run and identify the exact failed node.
- **FR-008**: Persisted log content MUST be concise and informative for end users, avoiding developer-only trace jargon while preserving actionable failure/success context.
- **FR-009**: Log entries MUST remain readable and actionable even if underlying objects are later modified, renamed, or removed.
- **FR-010**: The backend MUST store execution logs durably so they can be retrieved later by user-facing features.
- **FR-011**: Each logged execution step MUST include an outcome flag (`executed` or `not_executed`) distinct from final status to clearly represent skipped or blocked steps.
- **FR-012**: The log retention period MUST be configurable so deployments can choose how long execution logs are kept before automated cleanup.
- **FR-013**: For nested execution contexts, each relevant log entry MUST capture both the child object navigation path and parent hierarchy context so users can open the direct object and still understand where it ran.
- **FR-014**: Execution log details MUST mask or redact sensitive values while preserving enough context for end users to understand execution outcomes.
- **FR-015**: Log summaries MUST be limited to 240 characters, and each execution entry MUST include no more than 10 detail items unless a final item explicitly states additional details were truncated.
- **FR-016**: Historical log entries MUST remain actionable after referenced object rename or deletion by preserving immutable object identifier and display-name snapshot captured at execution time.

### Key Entities *(include if feature involves data)*

- **Execution Log Entry**: A persisted record of one execution attempt containing timestamp, status, object identity, hierarchy metadata, user-facing outcome summary, and optional relative authoring path.
- **Execution Object Reference**: Identifies the executed object (command or sequence) using stable identifiers and display name snapshot captured at execution time.
- **Execution Hierarchy Link**: Relationship metadata connecting a child command execution to a parent sequence execution and ordering context.
- **Execution Navigation Context**: Combined navigation data that includes a direct child object path and parent sequence hierarchy context for nested executions.
- **Execution Outcome Detail**: Concise, structured narrative of what happened during execution (e.g., detection found/not found, confidence values, coordinates used, skipped action reason).
- **Step Outcome Flag**: A per-step marker with values `executed` or `not_executed` that explains whether the step was actually performed regardless of final execution status.
- **Retention Policy**: A configurable rule defining how long execution log records are retained before expiration.
- **Sensitive Data Handling Rule**: A policy defining which execution detail fields require masking or redaction before persistence.

## Assumptions

- Existing backend execution signals already provide enough event data to derive user-oriented summaries without introducing developer-only debugging fields.
- Relative navigation paths are stored as route-like strings (for example, `/authoring/commands/{id}` and `/authoring/sequences/{id}`) so any hosting environment can resolve them.
- If both sequence and child command are relevant, entries include child path and parent reference; a UI can decide whether to open child directly or sequence context first.
- Initial scope is backend persistence and retrieval readiness only; no new UI behavior is required in this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of command and sequence execution attempts produce a persisted execution log entry.
- **SC-002**: In validation runs, users can identify the exact failed or successful execution object from log data alone in at least 95% of reviewed entries.
- **SC-003**: In validation runs involving sequence executions, users can determine where in the hierarchy a failure occurred in at least 95% of cases.
- **SC-004**: For parameterized actions and skipped detections, at least 95% of sampled log entries contain sufficient outcome detail for a user to explain what happened without consulting developer traces.
- **SC-005**: At least 95% of sampled execution entries comply with summary/detail conciseness bounds (summary <= 240 characters, <= 10 detail items with truncation marker when exceeded).
- **SC-006**: Operators can change retention duration through configuration and observe that newly expired entries are cleaned up according to the configured policy.
- **SC-007**: In privacy validation samples, no persisted execution log entry exposes unmasked sensitive values while still allowing users to determine what succeeded, failed, or was not executed.
