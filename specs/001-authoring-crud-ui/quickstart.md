# Quickstart: Authoring CRUD UI

**Branch**: 001-authoring-crud-ui  
**Date**: 2025-12-26

## Run the Web UI (dev)

1. Open a terminal at `C:/src/GameBot/src/web-ui`.
2. Start the dev server:

```bash
npm run dev
```

3. Open the app in your browser (Vite prints the URL). Navigate to the Authoring section.

## What you will see
- Navigation menu for object types: Actions, Commands, Games, Sequences, Triggers.
- List views with name and key attributes.
- Create/Edit forms with reference dropdowns showing names; IDs used in requests.
- Delete actions with confirmation; deletion blocked when referenced.
- Accessible form controls with labels, error states, and focus management. ErrorBoundary provides actionable guidance.

## Testing (frontend)
- Run unit tests:

```bash
npm test
```

- Add tests for list rendering, form validation, and delete confirmations.

## Notes
- No new dependencies planned; keep UI modular (components/pages/services).
- Performance budgets: list load p95 <1s for ≤500 items; create/edit p95 <3s.
