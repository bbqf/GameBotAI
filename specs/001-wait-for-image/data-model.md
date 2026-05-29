## Data Model: Wait for Image Primitive Action

## Entities

### 1. Executable Step Hosts

- **Purpose**: Represent executable units inside command definitions and sequence definitions.
- **Command-step changes**:
  - `CommandStep.type`: add `WaitForImage`
  - `CommandStep.waitForImage`: config payload used only when `type = WaitForImage`
- **Sequence-step changes**:
  - `SequenceStep` keeps its existing host shape
  - sequence primitive-action payloads gain a `WaitForImage` primitive action type with the same wait configuration
- **Existing relationships**:
  - a command step belongs to one command
  - a sequence step belongs to one sequence
  - both are ordered relative to sibling steps
- **Validation rules**:
  - `targetId` is unused for command `WaitForImage` steps
  - command `primitiveTap` must be null when `type = WaitForImage`
  - command `waitForImage` must be present when `type = WaitForImage`
  - sequence wait-step payloads must carry the same wait configuration fields as commands

### 2. WaitForImageConfig

- **Purpose**: Stores the authored parameters needed to pause until an image appears or timeout elapses.
- **Fields**:
  - `detectionTarget`: optional `DetectionTarget`
  - `timeoutMs`: integer timeout in milliseconds; defaults to `1000` when omitted on input
- **Relationships**:
  - owned by exactly one command step with `type = WaitForImage`, or by one sequence step primitive-action payload of type `WaitForImage`
- **Validation rules**:
  - `timeoutMs >= 0`
  - `detectionTarget.referenceImageId` may be absent only when the entire `detectionTarget` is absent
  - if `detectionTarget` is present and `confidence` is omitted, the existing detection default is applied
  - offsets and selection strategy follow existing `DetectionTarget` rules

### 3. DetectionTarget

- **Purpose**: Existing reusable image-detection configuration shared with tap and condition flows.
- **Relevant fields for this feature**:
  - `referenceImageId`
  - `confidence`
  - `offsetX`
  - `offsetY`
  - `selectionStrategy`
- **Feature-specific behavior**:
  - entire object is optional for `WaitForImage`
  - offsets may remain present for model consistency even if the initial UI only emphasizes image id and certainty

### 4. Wait Step Runtime Outcome

- **Purpose**: Represents the terminal result of one `WaitForImage` step execution.
- **Fields**:
  - `stepOrder`
  - `stepType = waitForImage`
  - `status`: `executed`, `completed_timeout`, or `completed_image_unavailable`
  - `reasonCode`: `image_detected`, `timeout_elapsed`, or `image_unavailable`
  - `reasonText`: optional human-readable explanation
  - `appliedDelayMs`: effective wait duration when available
- **Relationships**:
  - projected into execution-log step outcomes
  - associated with one command execution attempt or one sequence execution attempt

### 5. Execution Log Detail Attributes

- **Purpose**: Captures wait-step parameters and final exit condition in persisted execution history.
- **Attributes to add for wait steps**:
  - `timeoutMs`
  - `effectiveTimeoutMs`
  - `referenceImageId` when configured
  - `confidence` when configured or defaulted
  - `exitCondition`
  - `imageLoadStatus` when relevant
- **Validation rules**:
  - exactly one terminal `exitCondition` is recorded per wait step
  - `exitCondition = image_unavailable` remains distinct from `timeout_elapsed`

## State Transitions

### WaitForImage Step Lifecycle

1. `Authored`
   - step saved with optional image, optional certainty, and optional timeout input
2. `Normalized`
   - omitted timeout becomes `1000`
   - omitted certainty inherits the existing detection default when image is present
3. `Executing`
   - command executor enters the wait loop
4. Terminal state, exactly one of:
   - `ImageDetected`
   - `TimeoutElapsed`
   - `ImageUnavailable`

## Persistence Notes

- Commands continue to persist as file-backed JSON under `data/commands`.
- Sequences continue to persist as file-backed JSON under `data/commands/sequences`.
- No new repository or storage root is introduced.
- Execution logs continue to persist under `data/execution-logs`; the change is additive in per-step detail content only.