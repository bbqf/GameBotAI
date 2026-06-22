# Phase 1 Data Model: Queue Management Usability

This feature adds no persisted entities and no API schema changes. The "model" here is the
client-side decision logic and the UI-state additions needed for inline confirmations.

## Existing types (unchanged, for reference)

- `QueueSummary` (overview row): `id`, `name`, `emulatorSerial`, `cycleExecution`, `status`,
  `entryCount`. The `entryCount` field stays on the type; only its *display* (the Sequences column)
  is removed.
- `SaveQueueTemplate`: `{ name, entries, overwrite }` — sent to `POST /api/queue-templates`. Unchanged.
- `associatedTemplateName?: string` — the template the queue is currently linked to (held in
  `QueuesPage`; passed into `SaveTemplateDialog` as its `originName` prop). Drives the one-click
  decision.

## Template-save decision (one-click vs. confirm)

Inputs: `typedName` (trimmed), `originName` (the associated template name). No template-list fetch is
needed — the server's existing `409` distinguishes the non-associated cases.

```
norm(x) = x.trim().toLowerCase()

if norm(typedName) == norm(originName):
    → save(overwrite = true)                      # same (associated) template, one click
else:
    → save(overwrite = false)
       on success     → done                      # new name, one click (succeeds)
       on 409 (exists)→ show overwrite confirmation
                          on confirm → save(overwrite = true)   # different existing template, guarded
                          on cancel  → no-op
```

Validation rules (unchanged, applied before any save): name required; ≤ 100 chars; pattern
`^[A-Za-z0-9 _-]+$`.

## Inline confirmation UI state

A small result indicator co-located with each Save control. Conceptually:

- **Queue save result**: shown at the `QueueForm` action row. States: none / success message /
  error message. Success replaces the previous page-top "Queue updated/created successfully." banner
  for these saves.
- **Template save result**: shown at the `QueueTemplateControls` Save Template row. States: none /
  success ("Template \"X\" saved successfully.") / error.

State transitions:

| Trigger | Result state |
|---|---|
| Save clicked | clear previous result, attempt save |
| Save succeeds | success message at the control |
| Save fails (validation or request error) | error message at the control |
| Re-open form / new edit | result cleared |

No global/page-top banner is set for queue or template saves once this feature is in place (other
unrelated table messages — start/stop/load/delete — are out of scope and unchanged).

## Overview table column model

Columns after change: **Name, Emulator, Cycle, Status, Actions** (5 columns). The full-width rows
(loading, empty, live-schedule) use `colSpan = 5`.
