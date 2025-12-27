# Regression Pass: Unified Authoring UI

Scope: Actions, Commands, Triggers, Games, Sequences pages (unified layout).

## Scenarios to cover (manual)
- Actions: create, edit, delete; validate name required; description optional; list reload reflects changes.
- Commands: create/edit with steps (actions/commands), detection optional; reorder steps persists; validation blocks missing name or detection reference ID when set.
- Triggers: create/edit with criteria JSON, actions/commands arrays, optional sequence; invalid JSON blocks save; reorder persists.
- Games: create/edit with metadata key/values; validation on name; delete flow handles references.
- Sequences: create/edit with command steps; reorder persists; delete step.

## Execution notes
- Run against local dev (`npm run dev`) and backend (`dotnet run -c Debug --project src/GameBot.Service`).
- Use realistic sample data; confirm dropdowns populate; confirm create-new links open.
- Verify order persistence by saving, returning to list, re-opening item.
- Capture any failures with steps + expected vs actual; file issues with repro.

## Latest Run
- Date: __
- Tester: __
- Findings: __
- Issues filed: __
