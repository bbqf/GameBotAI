# Quickstart: Verify Execution Log Grid Cleanup

Manual and automated verification for the unified execution-logs grid.

## Prerequisites

- GameBot service running with the web-ui, and at least one executed **sequence** (invoking commands) and one **stand-alone command** in the execution logs.

## Run the web-ui tests

```powershell
cd src/web-ui
npm test            # Jest component tests (ExecutionLogsTree)
npm run lint        # eslint must be clean
npm run build       # tsc/vite type-check + build must pass
```

All three must pass before commit (Constitution: Testing Standards + Quality Gates).

## Manual walkthrough

1. **Single full-width grid** — Open the Execution Logs page. Confirm:
   - One grid spans the full content width; there is **no** separate "Execution Detail" panel.
   - Columns are: Expand · Timestamp · Name · Type · Status · Additional information. (US1 / FR-001, FR-002)

2. **Top-level rows** — Confirm a sequence row shows e.g. `… | Donate | Sequence | success | Sequence 'Donate' success with 7 steps executed.` and a stand-alone command shows its name, "Command", status, and summary. (FR-003, FR-004)

3. **Expand a stand-alone command** — Click its expand control; its sub-elements (taps, waits) appear as nested grid rows with the same columns. (US2 / FR-005)

4. **Second-level expansion inside a sequence** — Expand the sequence; expand a step that invoked a command; that command's own sub-elements appear one level deeper. (US2 / FR-006, FR-007)

5. **Independent expansion** — Expand two different top-level rows at once; collapsing one leaves the other expanded. (FR-007a / SC-008)

6. **No deep links** — Confirm no "Open in sequence" button appears on any row or sub-element. (US3 / FR-008, SC-004)

7. **Additional information** — Confirm condition/wait/delay details that used to live in the detail panel now appear in the Additional information column for the relevant nodes. (FR-009, SC-005)

8. **Live updates** — Start a longer sequence; while running, confirm the in-progress row updates and an expanded subtree refreshes within ~2 s without manual reload. (FR-011)

9. **Filter / sort / timestamp mode** — Exercise the filter inputs, column sort toggles, and exact/relative timestamp switch; confirm they still work over the grid. (FR-011, SC-006)

10. **Narrow width** — Shrink the viewport (or use device emulation). Confirm the grid scrolls horizontally, keeps all six columns at every level, and never opens a separate detail screen. (FR-012, SC-007)

## Success signal

All ten checks pass, the three npm commands are green, and the spec's Success Criteria SC-001…SC-008 are observable on screen.
