# Research: Any-of image condition and WaitForImage for a list parameter

## D-001: Where the list lives in the model

- **Decision**: Add `ImageIds` and `Match` to `ImageVisibleStepCondition`. Add `ReferenceImageIds` to `DetectionTarget` (used by `waitForImage` only).
- **Rationale**: The issue names `imageVisible.imageIds` and `match`. `DetectionTarget` is the existing holder of the WaitForImage image id, and the overlay key is already `waitForImage.detectionTarget.referenceImageId`.
- **Alternatives considered**: A new condition type (refused: non-goal). A new top-level step field (refused: breaks the detection-target model).

## D-002: How a list placeholder reaches the run

- **Decision**: The resolvers expand a whole `{{name}}` placeholder of a multi-value parameter into a list in the two allowed positions. The scope text (items joined by `|`) is split back to a list.
- **Rationale**: Feature 179 keeps the scope text-based and refuses `|` in items, so the split is exact. No change to the scope engine.
- **Alternatives considered**: Change the scope to carry lists (refused: large change, outside the issue).

## D-003: Any-of evaluation

- **Decision**: Check items in list order and stop at the first match. Missing images never match.
- **Rationale**: A stale id in an operator list must not fail the run. First-match gives a stable `matchedImageId`.
- **Alternatives considered**: Check all items and pick the best score (refused: more cost, another match mode in effect).

## D-004: WaitForImage polling

- **Decision**: Keep one poll loop. In each pass, detect each loaded item in list order. Stop at the first found.
- **Rationale**: The timeout, poll interval, and schedule rules (features 166, 175) apply to the list as a whole.
- **Alternatives considered**: One loop per item (refused: the timeout would not be shared).

## D-005: Save-time refusal

- **Decision**: Add a flag `AllowsList` to `ParameterReference`. The parameter validation gives an error for a multi-value parameter in a position with `AllowsList = false` when the placeholder is the whole value.
- **Rationale**: The scanner already knows each field path. The validation service already knows the declarations. Feature 179 made this a run-time failure only; the issue asks for a save error.
- **Alternatives considered**: Keep the run-time failure only (refused: the issue says "refused at save").

## D-006: The `match` field

- **Decision**: Store `match` as optional text. The save accepts only `any` (case-sensitive) and refuses other values. A null value means `any`.
- **Rationale**: Keeps room for future modes without a schema change, and adds none now (non-goal).
- **Alternatives considered**: No `match` field (refused: the issue lists it).

## D-007: Web UI

- **Decision**: Change the editor only if it blocks a multi-value image parameter as the image key of a WaitForImage step or an `imageVisible` leaf. The task list contains a check task first.
- **Rationale**: The image field already stores free text with placeholders (features 114 and 179). The issue does not ask for new editor controls.
- **Alternatives considered**: A full multi-image editor for written lists (refused: not in the issue).
