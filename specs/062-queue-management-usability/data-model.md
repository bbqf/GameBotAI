# Phase 1 Data Model: Queue Management Usability

This feature adds no persisted entities and no API schema changes. The "model" here is the
client-side decision logic and the UI-state additions needed for inline confirmations.

## Existing types (unchanged, for reference)

- `QueueSummary` (overview row): `id`, `name`, `emulatorSerial`, `cycleExecution`, `status`,
  `entryCount`. The `entryCount` field stays on the type; only its *display* (the Sequences column)
  is removed.
- `SaveQueueTemplate`: `{ name, entries, overwrite }` — sent to `POST /api/queue-templates`. Unchanged.
- `associatedTemplateName?: string` — the template the queue is currently linked to (held in
  `QueuesPage`; passed into `QueueTemplateControls`). Pre-fills the editable name field and is the
  name used by the one-click **Save Template** action.

## Template-save controls and decision (one-click vs. confirm)

There are two distinct save triggers; neither needs a template-list fetch — the server's existing
`409` distinguishes the collision case.

`originName` = the associated template name; `typedName` = the editable name field (trimmed).

**Save Template (bottom button)** — quick re-save to the associated template:

```
if originName is set:
    → save(name = originName, overwrite = true)   # one click, no prompt; ignores typedName
else:
    → disabled                                    # no template yet; create one via Rename
```

**Rename (next to the name field, in the template area)** — save under the typed name:

```
norm(x) = x.trim().toLowerCase()

if norm(typedName) == norm(originName):
    → save(name = typedName, overwrite = true)    # same (associated) template, one click
else:
    → save(name = typedName, overwrite = false)
       on success     → done                       # new name, one click (succeeds)
       on 409 (exists)→ show overwrite confirmation NEXT TO Rename
                          on confirm → save(name = typedName, overwrite = true)  # different template, guarded
                          on cancel  → no-op
```

Validation rules (applied before a Rename save): name required; ≤ 100 chars; pattern
`^[A-Za-z0-9 _-]+$`. Opening the template area resets `typedName` to `originName` so an unconfirmed
edit does not linger.

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
