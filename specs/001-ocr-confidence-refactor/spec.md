# Feature Specification: OCR Confidence via TSV

**Feature Branch**: `001-ocr-confidence-refactor`  
**Created**: 2025-11-26  
**Status**: Draft  
**Input**: User description: "Improvment in OCR is needed. Calculating Confidence is wrong, tesseract should be started with the parameter tsv that reports the correct confidence. In order to make use of it, refactoring of tesseract output is required."

## User Scenarios & Testing (mandatory)

### User Story 1 - Accurate OCR confidence (Priority: P1)

As a rule developer, I need OCR evaluations to report correct per-word confidence so that triggers based on text reliability make correct decisions.

**Why this priority**: Incorrect confidence leads to false positives/negatives in automation decisions.

**Independent Test**: Provide a fixed image with known readable text; evaluation returns per-word confidences matching Tesseract TSV conf field and aggregates consistently.

**Acceptance Scenarios**:

1. Given a test image with clear printed text, When OCR evaluation runs, Then TSV output is parsed and the reported confidences equal TSV `conf` values for corresponding tokens.
2. Given mixed-quality text (some blurred), When OCR evaluation runs, Then low-confidence tokens have lower numeric confidence than clear tokens and aggregate reflects weighted average rules.

---

### User Story 2 - Configurable aggregation (Priority: P2)

As a rule developer, I want a consistent, documented confidence aggregation for multi-token matches so that trigger thresholds are predictable.

**Why this priority**: Different aggregations change trigger results; consistency is required.

**Independent Test**: For a known TSV row set, the computed aggregate confidence matches the documented formula.

**Acceptance Scenarios**:

1. Given tokens with confidences [95, 80, 70], When aggregated, Then the final confidence equals the defined method (default: arithmetic mean of matched tokens, excluding -1/noise).

---

### User Story 3 - Backward-safe output contract (Priority: P3)

As a consumer of OCR results, I need the output schema to include per-token confidence and overall confidence without breaking existing fields.

**Why this priority**: Avoid downstream breaking changes while enabling better decisions.

**Independent Test**: Schema validation passes; existing fields preserved; new fields present with correct values.

**Acceptance Scenarios**:

1. Given prior JSON structure, When the new evaluator runs, Then the previous fields are present and populated; additional `tokens[].confidence` and `confidence` are included.

### Edge Cases

- Empty or non-text images produce zero tokens and confidence is 0 with reason "no_text_detected".
- TSV `conf` may be -1 for noise; these tokens are excluded from aggregation but retained in tokens with confidence = -1.
- Multi-line and multi-block text preserves reading order; aggregation only considers matched tokens per query/match scope.

## Requirements (mandatory)

### Functional Requirements

- FR-001: System MUST invoke Tesseract with TSV output mode for OCR confidence extraction.
- FR-002: System MUST parse TSV output and map fields (level, page_num, block_num, par_num, line_num, word_num, left, top, width, height, conf, text).
- FR-003: System MUST populate per-token confidence from TSV `conf` as integer 0-100 (or -1 for noise).
- FR-004: System MUST compute overall confidence for a match as arithmetic mean of included token confidences, excluding -1 values.
- FR-005: System MUST expose both overall confidence and tokens[].confidence in the OCR evaluation result contract.
- FR-006: System MUST return a deterministic confidence for identical inputs.
- FR-007: System MUST treat empty or whitespace-only tokens as excluded from aggregation.
- FR-008: System MUST fail gracefully with descriptive reason if Tesseract execution fails (non-zero exit) and set confidence to 0.
- FR-009: System MUST document aggregation method in specs and tests.

### Key Entities

- OCRToken: text, bbox (left, top, width, height), confidence, line, order.
- OcrEvaluationResult: tokens[], confidence (0-100), text (full concatenation), reason.

## Success Criteria (mandatory)

### Measurable Outcomes

- SC-001: For test images with known quality, token confidences equal TSV `conf` values within exact match (integer) for 95% of tokens.
- SC-002: Aggregate confidence differs from a baseline heuristic by >30% on low-quality samples, reducing false positives by 50% in trigger tests.
- SC-003: 100% of OCR evaluations complete with non-zero TSV rows on test assets return confidence between 0 and 100 with no parsing errors.
- SC-004: Downstream tests consuming OCR results remain passing; no breaking schema changes.# Feature Specification: [FEATURE NAME]

**Feature Branch**: `[###-feature-name]`  
**Created**: [DATE]  
**Status**: Draft  
**Input**: User description: "$ARGUMENTS"

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

### User Story 1 - [Brief Title] (Priority: P1)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently - e.g., "Can be fully tested by [specific action] and delivers [specific value]"]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- What happens when [boundary condition]?
- How does system handle [error scenario]?

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
- **FR-002**: System MUST [specific capability, e.g., "validate email addresses"]  
- **FR-003**: Users MUST be able to [key interaction, e.g., "reset their password"]
- **FR-004**: System MUST [data requirement, e.g., "persist user preferences"]
- **FR-005**: System MUST [behavior, e.g., "log all security events"]

*Example of marking unclear requirements:*

- **FR-006**: System MUST authenticate users via [NEEDS CLARIFICATION: auth method not specified - email/password, SSO, OAuth?]
- **FR-007**: System MUST retain user data for [NEEDS CLARIFICATION: retention period not specified]

### Key Entities *(include if feature involves data)*

- **[Entity 1]**: [What it represents, key attributes without implementation]
- **[Entity 2]**: [What it represents, relationships to other entities]

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: [Measurable metric, e.g., "Users can complete account creation in under 2 minutes"]
- **SC-002**: [Measurable metric, e.g., "System handles 1000 concurrent users without degradation"]
- **SC-003**: [User satisfaction metric, e.g., "90% of users successfully complete primary task on first attempt"]
- **SC-004**: [Business metric, e.g., "Reduce support tickets related to [X] by 50%"]
