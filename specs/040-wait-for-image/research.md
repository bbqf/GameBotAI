## Research: Wait for Image Primitive Action

### Decision 1: Reuse `DetectionTarget` inside a dedicated wait-step config

- **Decision**: Model the optional image reference and optional certainty for the new step by reusing the existing `DetectionTarget` shape inside a new `WaitForImageConfig` wrapper that also carries timeout.
- **Rationale**: `DetectionTarget` already represents image reference, confidence, offsets, and selection semantics used elsewhere in the product. Reusing it preserves authoring and validation consistency while keeping timeout orchestration separate from image-match configuration.
- **Alternatives considered**: A brand-new image-wait DTO was rejected because it would duplicate confidence validation and fragment the UX; flattening image fields directly into `CommandStep` was rejected because it weakens cohesion and does not match current config patterns.

### Decision 2: Add a dedicated `WaitForImage` primitive step across commands and sequences

- **Decision**: Extend `CommandStepType` with a `WaitForImage` variant for commands, and extend sequence primitive-action payload handling so sequence steps can also author and execute `WaitForImage`.
- **Rationale**: Current command execution and sequence authoring logic already dispatch on explicit step or primitive-action types. Extending both surfaces is the smallest additive change that preserves type safety, validation clarity, and explicit runtime semantics across the full requested scope.
- **Alternatives considered**: Reusing `PrimitiveTap` with special-case config was rejected because it muddies behavior and validation; a larger refactor to a polymorphic primitive-action hierarchy was rejected as unnecessary for one new step type.

### Decision 3: Keep wait execution in existing command and sequence runners and reuse the screenshot pipeline

- **Decision**: Implement wait behavior in the existing command and sequence execution paths using `Task.Delay`, polling against the current screenshot/template-matching services at the configured capture interval.
- **Rationale**: `CommandExecutor` and `SequenceRunner` already own orchestration for their respective flows. Reusing the same detection services avoids a new subsystem and keeps wait semantics consistent across commands and sequences.
- **Alternatives considered**: A separate wait orchestration service was rejected as extra abstraction without distinct reuse value; blocking sleep or synchronous polling was rejected because it conflicts with the existing async execution path.

### Decision 4: Treat timeout and image-unavailable as normal completion outcomes

- **Decision**: Record wait-step completion as one of three terminal exit conditions: image detected, timeout elapsed, or image unavailable, with timeout and image unavailable treated as non-error completions.
- **Rationale**: This matches the clarified spec and preserves the feature’s core goal: waiting should not fail the command when the image never appears or cannot be loaded. The execution log still captures the exact exit condition for diagnosis.
- **Alternatives considered**: Treating image unavailable as an immediate failure was rejected because it violates the requested behavior; collapsing image unavailable into timeout was rejected because it would hide a materially different root cause in logs.

### Decision 5: Reuse existing web UI image/confidence patterns for authoring

- **Decision**: Extend the existing command form with wait-step controls that reuse current image-selection and similarity/confidence entry patterns, plus a numeric timeout input with a 1000 ms default.
- **Rationale**: The web UI already exposes image and confidence inputs in command and sequence authoring flows. Reusing that mental model reduces UI churn and keeps validation messages familiar.
- **Alternatives considered**: A dedicated modal or separate authoring page was rejected because the feature belongs to inline step authoring; making image mandatory was rejected because the spec requires a pure time-based wait path.

### Decision 6: Extend existing command, sequence, and execution-log APIs instead of adding new endpoints

- **Decision**: Add the new step type and config to existing command create/update/get/list/execute contracts, extend sequence step contracts for wait-step authoring and execution, and extend execution-log detail payloads to expose wait parameters and exit condition through structured detail attributes.
- **Rationale**: The feature fits current command, sequence, and execution-history flows. Additive schema updates preserve client compatibility and avoid new endpoint surface area.
- **Alternatives considered**: A dedicated wait-step validation endpoint was rejected because current command validation already happens inline; a separate wait-log resource was rejected because execution log detail already models per-step outcomes and structured details.

### Decision 7: Cover the feature with unit, integration, contract, and web UI verification

- **Decision**: Add backend unit tests for wait execution behavior in commands and sequences, backend integration tests for command and sequence persistence/validation/logging, contract coverage for additive API shape, and web UI/service verification for DTO and form round-tripping.
- **Rationale**: The constitution requires deterministic coverage for executable logic and externally visible contracts. This feature touches command execution, sequence execution, API mapping, and user-visible log rendering, so it needs coverage at each boundary.
- **Alternatives considered**: Integration-only coverage was rejected because it would make executor logic slower to debug and less isolated; backend-only coverage was rejected because the authoring UI and execution-log display are explicit scope.