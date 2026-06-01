# Quickstart / Manual Verification: Queue–Template Link with Auto-Load

Prereqs: run the GameBot service + web-ui; have at least one sequence authored and an
emulator queue created. No visual changes are expected — these steps verify **behavior**.

## 1. Establish a link by loading a template (US2)

1. Create/save a template "Daily Farm" with entries A, B, C (Save Template in the editor).
2. Open a different queue Q2 (empty). Load "Daily Farm" via the template picker.
3. Q2 now shows A, B, C and the template-name button reads **Daily Farm**.
4. ✅ Behind the scenes the queue is now linked: `GET /api/queues/{Q2}` returns
   `linkedTemplateId` = the template's id and `linkedTemplateName` = "Daily Farm".

## 2. Establish a link by saving (US2)

1. Open an empty queue, add entries X, Y, save them as a new template "Patrol".
2. ✅ `GET /api/queues/{id}` now reports `linkedTemplateName` = "Patrol" (saving associated
   the queue with the saved template), with **no new on-screen control** used.

## 3. Auto-load after restart (US1 — the core behavior)

1. With Q2 linked to "Daily Farm" (step 1), **restart the service** (runtime entries are
   discarded by design).
2. Open Q2 in the editor.
3. ✅ Q2's entries are A, B, C again — auto-loaded, no manual Load clicked.
4. ✅ Press **Start** on Q2 — it runs with the auto-loaded entries (the entries are real
   server-side runtime entries, not a view-only fill).

## 4. Replace the link (US2)

1. With Q2 linked to "Daily Farm", load a different template "Night Run" into Q2.
2. ✅ `linkedTemplateName` becomes "Night Run"; after a restart, opening Q2 auto-loads
   Night Run's entries (the prior link was replaced).

## 5. Shared template, independent queues (US2 / SC-004)

1. Link both Q1 and Q2 to "Daily Farm".
2. Edit Q1's entries (remove one) — do **not** save back to the template.
3. ✅ "Daily Farm" is unchanged; Q2 (and the template) are unaffected. After a restart both
   queues auto-load the template's current contents independently.

## 6. Edits within a session are not clobbered (FR-012)

1. Open a linked queue (auto-loads). Remove all its entries one by one.
2. ✅ The queue stays empty — it is **not** re-filled by auto-load (auto-load only runs on
   the first open after a restart, not after a deliberate clear).
3. Use **Reload Template** (048) to deliberately re-apply the template if desired.

## 7. Broken link clears itself (FR-011)

1. Link a queue to a template, then **delete that template**.
2. Open the queue.
3. ✅ The queue opens with no entries and no error; the template-name button reads
   **(no template)**. `GET /api/queues/{id}` now returns `linkedTemplateId: null`
   (the broken link was cleared and persisted).
4. Note: a *rename* of the template does **not** break the link (links are by ID).

## 8. Running queue is not disturbed (FR-010)

1. Start a linked queue, then open it in the editor while it is Running.
2. ✅ The currently running entries are shown unchanged (no auto-load replacement).

## Known limitation (by design, Q3)

Starting a linked queue **directly from the list** right after a restart (without opening
its editor first) runs it empty — auto-load is scoped to opening the queue's edit/detail
page. Open the queue once to materialize its entries.

## Automated checks to run before commit (constitution gate)

- Backend: `dotnet test` (unit + integration + contract for Queues).
- Frontend: `npm test` in `src/web-ui` (queues service + `QueuesPage.link` specs).
- Lint/format/static analysis clean on both tiers.
