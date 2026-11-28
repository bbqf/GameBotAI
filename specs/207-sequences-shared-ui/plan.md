# Implementation Plan: Move the Sequences list, form page, and delete to the shared UI components

**Branch**: `207-sequences-shared-ui` | **Date**: 2026-11-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/207-sequences-shared-ui/spec.md`

## Summary

Use the patterns of feature 206 on the Sequences page: `PageHeader`, the message area, the filter bar, `ListView`
with Edit and "⋮", one card form (880 px) with a back arrow, "Step through" in the header, and `FooterBar`, and the
shared delete flow. Merge the two copies of the form into one render path. The step rows and editors stay.

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3  
**Primary Dependencies**: Vite 8; shared components in `src/web-ui/src/components/ui/`; `components/authoring/useDeleteFlow.ts` and `DeleteReferences.tsx` (feature 206); `@dnd-kit` (no change); no new dependency  
**Storage**: N/A  
**Testing**: Jest 29 + Testing Library (`src/pages/__tests__/SequencesPage*.spec.tsx`, `SequencesPageWaitFor*.test.tsx`, `CommandsAndSequencesPrimitiveAuthoring.spec.tsx`, and other tests that open the page); Playwright 1.49 (project `chromium`, 1280 px)  
**Target Platform**: Current desktop browsers (Authoring is desktop-only on the phone)  
**Project Type**: Web application (frontend only: `src/web-ui`)  
**Performance Goals**: The edit page opens with the same calls as now; a row click shows the page header less than 100 ms after the `get` answer.  
**Constraints**: The payloads do not change (FR-007). Tokens only in CSS; hover rules only inside `@media (hover: hover)`. No backend change.  
**Scale/Scope**: 1 page (about 2,900 lines; the merge removes about 300), 1 hook change (`kind: 'sequence'`), 1 CSS modifier, about 16 changed test files, 1 new browser test file, 1 changed mock helper.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: Authoring keeps the phone support level `desktop-only`. The phone does not render this page.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | One form instead of two copies (less code). The old table and the old modal go away from the page. |
| II. Testing | Pass | Changed Sequences tests; new tests for the payload rules, the list, and the delete flow; one new browser test file. |
| III. UX consistency | Pass | Same patterns as feature 206. Phone level stated. |
| IV. Performance | Pass | No new call. |
| V. Living documentation | Pass | `docs/ui-guidelines.md` section 13 and "Last reviewed"; `specs/STATUS.md` line 207. |
| VI. STE | Pass | New UI text: "Create sequence", "New sequence", "No sequences.", "Delete sequence <name>?", "Sequence created." and the other messages. |
| Version bump | Pass | Done in phase 2 before the push. |

## Project Structure

### Documentation (this feature)

```text
specs/207-sequences-shared-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/sequences-layout.md
└── tasks.md
```

### Source Code

```text
docs/ui-guidelines.md                                   # section 13 (ui-form--wide), Last reviewed

src/web-ui/src/
├── components/ui/ui.css                                # .ui-form--wide (max-width 880 px)
├── components/authoring/useDeleteFlow.ts               # kind 'sequence'
├── components/authoring/__tests__/useDeleteFlow.test.tsx   # default text of 'sequence'
├── pages/SequencesPage.tsx                             # list page, one form, delete flow
├── styles.css (and other CSS files)                    # remove the unused Sequences rules
└── pages/__tests__/SequencesPage*.spec.tsx, ...        # changed
src/web-ui/tests/e2e/
├── helpers/authoringMock.ts                            # sequences routes
└── authoring-sequences-layout.spec.ts                  # new (chromium)
```

## Design

### Merge of the two forms

- Read the create form (`creating && <form …>`) and the edit form (`editingId && <section><h3>Edit Sequence</h3><form …>`)
  side by side first. List each difference in the implementation notes before the change (ids, `disabled` rules,
  handlers, texts). Each difference that is not the title, the save call, the conflict handling, or Delete must be
  kept by a condition, or must be a bug that the notes name.
- Make one function `renderForm()` (or a component in the same file) that renders the form for
  `mode = creating ? 'create' : 'edit'`. Use the create ids (`sequence-basics`, `sequence-parameters`,
  `sequence-steps`, and the create field ids) for both modes.
- `handleSubmit`: `validate(form)` → errors; build `linearPayload` (as now) → `validatePerStepConditions`; then:
  - create: `createSequence({ name, version, steps, interStepDelayRangeMs, ...(parameters.length > 0 ? { parameters } : {}), ...(excludeFromSuccessNotifications ? { excludeFromSuccessNotifications: true } : {}), ...(ignoreSmartScheduling ? { ignoreSmartScheduling: true } : {}), ...(onTimeLimit === 'end' ? { onTimeLimit: 'end' } : {}) })`;
    success → close, reset, `setPendingStepId(undefined)`, "Sequence created.", reload; error → the parameter error
    summary or the message or "Failed to create sequence".
  - edit: `updateSequence(editingId, { name, version, steps, interStepDelayRangeMs, parameters, excludeFromSuccessNotifications, ignoreSmartScheduling, onTimeLimit })`;
    success → reload, close, reset, "Sequence updated."; `isSequenceConflictError` → the conflict text and
    `loadSequenceIntoForm(editingId)`; other error → the summary or the message or "Failed to update sequence".
  - Keep the comments of features 078, 122, 170, and 189 at the payload members.
- `loadedName`: set in `loadSequenceIntoForm` from the loaded name; cleared on reset.

### Page layout

- List page (`!editorOpen`): `PageHeader title="Sequences" actions={<Button variant="primary" icon="add" onClick={startCreate}>Create sequence</Button>}`;
  `<div className="ui-message-area">` with `tableMessage` (`role="status"`) and `tableError` (`role="alert"`, only when
  the list has data); `<div className="ui-filter-bar"><input aria-label="Filter by name" placeholder="Filter by name" disabled={loading} /></div>`;
  `ListView` with `ariaLabel="Sequences table"`, `tableClassName="sequences-list"`, `rowTestId` `sequence-row-<id>`,
  columns:
  - Name: flexible, `text` = name.
  - Options: width 360, render `<span className="ui-muted">{texts.join(', ')}</span>` or "—"; `text` = the help texts
    joined with "\n" (tooltip). Keep the classes `sequence-badge--no-success-notifications` and
    `sequence-badge--ignores-smart-scheduling` on inner spans if a test uses them (search first).
  - Steps: width 96, `align: 'end'`, `<span className="ui-num">`.
  - `rowActions`: Edit (icon `edit`, label "Edit <name>", `openEdit`) and the menu with Delete (danger,
    `del.open({ id, name })`); `menuLabel` "More actions for <name>"; `onRowClick = openEdit`.
  - `state`: loading → `{ kind: 'loading' }`; error with no data → `{ kind: 'error', text, onRetry: reloadSequences }`;
    no rows → `{ kind: 'empty', text: 'No sequences.' }`; else `'ready'`.
- Form page (`editorOpen`): `PageHeader onBack={backToList} title={creating ? 'New sequence' : (loadedName ?? 'Loading…')} actions={<Button variant="secondary" icon="play" testId="step-through-button" …>Step through</Button>}`
  (`aria-describedby="step-through-reason"` when a reason exists; `Button` has `ariaLabel` but not `ariaDescribedBy`:
  add an optional `ariaDescribedBy` prop to `Button` if needed); `<div className="ui-message-area">` with the reason
  `<span id="step-through-reason" className="form-hint">`; `<div className="ui-form ui-form--wide">` with the one form;
  inside the form, after the sections and the form error: `FooterBar left={<>{mode === 'edit' && <Button variant="danger-outline" onClick={() => del.open({ id: editingId, name: loadedName ?? form.name })} disabled={submitting}>Delete</Button>}{loading && <span className="form-hint">Loading…</span>}</>} right={<><Button variant="secondary" onClick={cancel} disabled={submitting}>Cancel</Button><Button variant="primary" type="submit" disabled={submitting}>Save</Button></>}`.
  `StepThroughPanel` renders after the form as now (edit mode).
- The add-step toolbar: each `<button>` becomes `<Button variant="secondary" testId=… onClick=… disabled=…>` with the
  same text. "Add to steps" keeps its disabled rule `!pendingStepId`.
- Remove: `<h2>`, the hint "No sequences yet…", the old table, "← Back to list", `<h3>Edit Sequence</h3>`, the `hr`
  elements, `FormActions`, `ConfirmDeleteModal`, `deleteOpen`, `deleteMessage`, `deleteReferences`.

### Delete

- `useDeleteFlow` gets `kind: 'game' | 'command' | 'sequence'`; the default 409 text for `sequence` is "Cannot delete:
  the sequence is in use. Remove the links first."; the default error "Failed to delete sequence".
- `onDeleted`: if the edit page shows the deleted id → close the form, reset, `setDirty(false)`; reload; "Sequence
  deleted.". `onError`: list page → `tableError`; edit page → `setErrors({ form })`.
- `ConfirmDialog` as feature 206 with the title "Delete sequence <name>?".

### CSS

- `ui.css`: `.ui-form--wide { max-width: 880px; }` and `.ui-muted { color: var(--gb-color-text-muted); }` (new).
- Remove the rules of `sequences-table`, `sequences-row`, `sequence-steps-separator`, `sequence-badge*` (if no code
  uses them), and `link-button` / `actions-header` only if no other code uses them. Search each class first.

## Complexity Tracking

No constitution violations.
