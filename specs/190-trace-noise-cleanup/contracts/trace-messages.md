# Contract: Trace Messages

The service has no new or changed API. The only external interface that changes is the console trace output. This contract states what the operator sees after the change.

## C-001: Image-match evaluation

- At each trace level, an image-match evaluation writes no line about its duration.
- The "Image match for '{ReferenceImageId}' was satisfied by alternate reference …" message (Information) does not change.

## C-002: PrimitiveTap step with no target found

- At Warning level (the default) and at Information level, a PrimitiveTap step that does not find its target after all retry cycles writes no line.
- At Debug level and at Trace level, the step writes one line with event id 6012 and this text: "PrimitiveTap step {StepOrder}: target not detected after {TotalCycles} retry cycles."
- The execution log entry of the step does not change.

## C-003: Removed event ids

- The service does not write event ids 6002, 6006, or 6007. It did not write them before this change either, because no code called them.

## C-004: All other trace messages

- All other trace messages keep their event id, level, and text.
