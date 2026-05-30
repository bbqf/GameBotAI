# Quickstart: Unified Authoring Object Pages

1. **Install deps**
   - Node 18+; run `npm install` in `src/web-ui`.
2. **Run dev server**
   - From `src/web-ui`: `npm run dev`.
3. **Backend API**
   - Ensure GameBot Service running (`dotnet run -c Debug --project src/GameBot.Service`) so dropdowns populate.
4. **Develop the unified pages**
   - Reuse Action layout components for Commands, Triggers, Game profiles, Sequences.
   - Implement reference dropdowns with inline “Create new” modal/panel and auto-select.
   - Implement array add/edit/delete/reorder with drag-and-drop and order preview.
   - Ensure Save/Cancel immediately writes live changes and shows confirmation.
5. **Run tests**
   - UI unit: `npm run test` (Vitest/RTL) in `src/web-ui`.
   - E2E (if available): run Playwright suite covering array reorder and inline create-new.
6. **Validate**
   - Manual: create Command via unified page without touching JSON/IDs; reorder steps; save; reopen to confirm order.
   - Check performance budgets: form edits ≲100 ms, reorder ≲200 ms, initial load <1.5 s.
