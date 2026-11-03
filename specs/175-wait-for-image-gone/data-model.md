# Data Model: Wait Until an Image Is Gone

## WaitForImage step (changed)

A sequence step with `stepType` `Action` and an action of type `WaitForImage`. The fields are in
`action.parameters` (stored) and `primitiveAction.payload` (API request). Two optional fields are new.

| Field | Type | Required | Default | Rule |
|-------|------|----------|---------|------|
| `detectionTarget` | object | no | none | Not changed. `referenceImageId`, `confidence`, `offsetX`, `offsetY`, `selectionStrategy`, `region`. |
| `timeoutMs` | integer | no | 1000 | Not changed. 0 or more. |
| `until` | string | no | `present` | `present` or `absent`, not case-sensitive. New. |
| `absentCaptures` | integer | no | 2 | 1 or more. Used only when `until` is `absent`. New. |

A numeric field can hold a `{{name}}` parameter reference. The save check skips the field.

## Outcomes

| Mode | Outcome | When | Step status |
|------|---------|------|-------------|
| present | `image_detected` | The image is seen. | Succeeded |
| present | `timeout_elapsed` | `timeoutMs` passes first. | Succeeded |
| present | `image_unavailable` | No capture source or reference image. | Succeeded |
| present | `delay_elapsed` | No image id given (plain delay). | Succeeded |
| absent | `image_absent` | `absentCaptures` consecutive captures lack the image. | Succeeded |
| absent | `timeout_elapsed` | `timeoutMs` passes first. | Succeeded |
| absent | `image_unavailable` | No capture source or reference image. | Succeeded |

`stepOutcomes` (for later conditions) records `success` in all cases, as before.

## New and changed types

- `WaitForImageConfig` (Domain): new properties `Until` (string, default `present`) and `AbsentCaptures`
  (int, default 2).
- `WaitForImageUntil` (Domain.Commands.Execution): constants `Present`, `Absent`, `DefaultAbsentCaptures`;
  `TryParse(string?, out bool isAbsent)`.
- `AbsentCaptureCounter` (Domain.Commands.Execution): constructor takes the needed count. Method
  `Observe(PassKind)` returns true when the needed count is reached.
- `PassKind`: `Seen`, `Absent`, `Failed`.
- `WaitForImagePayloadContract` (Service): new properties `Until` and `AbsentCaptures`.

## State transitions (counter)

```text
count = 0
Seen   --> count = 0
Failed --> count = 0
Absent --> count = count + 1
           if count >= absentCaptures -> IMAGE_ABSENT
any state --now >= deadline--> TIMEOUT_ELAPSED
```
