# Unified Authoring UI Audit Checklist

Use this checklist to verify consistency across Action, Command, Trigger, Game, and Sequence authoring pages. Run after UI changes or before releases. Mark each item per page.

## Quick Instructions
- Open each page in the web UI and validate against the criteria below.
- Confirm dropdowns populate from backend data; stub services only if necessary.
- Reorder/array checks should verify order persistence after save and reload.
- Record any gaps and file follow-up issues.

## Checklist
- **Layout parity**: Sections follow shared order (Basics → References/Steps → Arrays/Metadata → Final actions). Save/Cancel aligned at the bottom with Delete on edit forms.
- **Basics section**: Required name field with inline validation message; error text blocks save until resolved.
- **Reference dropdowns**: Searchable, show human labels, and include “Create new” affordance (opens new tab/panel). No raw ID inputs.
- **Array controls**: Add, delete, and reorder available; empty state messaging present; order shown and preserved on save/reload.
- **Loading affordances**: Loading hint visible when data is fetching or submitting; buttons disabled during submit.
- **Error surfacing**: API errors surfaced inline (FormError); validation errors appear near fields.
- **Save behavior**: Save writes immediately; success reflected in list reload; no draft/publish path.
- **Delete flow**: Delete button present on edit forms; ConfirmDeleteModal shown; reference blocking surfaced with message + references list.
- **Navigation consistency**: Tabs/entry points route to unified pages (no legacy layouts accessible).
- **Accessibility**: Labels tied to inputs; aria-invalid set on error; buttons have accessible names (Move up/down, Delete) for lists.

## Page-Specific Notes
- **Action**: Parameters section uses shared layout; confirm Create Action aligns with basics + parameters + final actions.
- **Command**: Steps (actions/commands) and Detection sections present; reorder and delete steps; detection fields optional with validation on reference image ID when set.
- **Trigger**: Actions/Commands arrays reorderable; Sequence dropdown optional; criteria JSON textarea with error message on invalid JSON.
- **Game**: Basics + Metadata section; metadata rows add/delete; no raw JSON entry needed.
- **Sequence**: Steps array for commands; reorder preserves order on save/reload.

## Signoff
- **Date/Reviewer**:
- **Scope** (pages audited):
- **Findings/Gaps**:
- **Follow-ups filed** (issue links):
