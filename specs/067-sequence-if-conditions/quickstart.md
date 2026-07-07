# Quickstart: If-Then-Else Conditions in Sequences (067)

## Build & test (quality gate)

```powershell
# Backend
dotnet build c:\src\GameBot\GameBot.sln
dotnet test c:\src\GameBot\tests\unit

# Web UI (gate = vite build + jest; lint/tsc have pre-existing failures)
npm --prefix c:\src\GameBot\src\web-ui run build
npm --prefix c:\src\GameBot\src\web-ui test
```

## Try it via API

1. Start the service (`dotnet run --project c:\src\GameBot\src\GameBot.Service`).
2. Create a sequence with an if block (see `contracts/sequences-api.md` for the full payload) — condition `imageVisible` on an existing image, one command in `body`, optionally `elseBody`.
3. `POST /api/sequences/{id}/execute`, then inspect `GET /api/execution-logs` → subtree: expect an `if` node whose message names the branch taken, followed by that branch's step nodes.

## Try it via UI

1. `npm --prefix c:\src\GameBot\src\web-ui run dev`, open Sequences, create/edit a sequence.
2. In the add-step area the middle column reads **Loops and Conditions** with buttons Count / While / Repeat‑Until / **If**.
3. Add an If block → configure its condition with the same controls as a While loop header; add steps to the then area; click **Add else** to reveal the else area.
4. Inside a loop body, use the body's **If** button to nest a conditional (branches themselves cannot contain loops or ifs — the buttons aren't offered there).

## Key test scenarios (map to spec acceptance)

| Scenario | Where |
|----------|-------|
| Condition true → then runs; false → then skipped | `tests/unit/Sequences/SequenceRunnerIfTests.cs` |
| Else branch selection, no-op when branch absent/empty | same |
| Condition error fails step + sequence (while-loop parity) | same |
| If inside loop: re-evaluated per iteration; break in branch exits loop; `{{iteration}}` substitution in branch steps | same |
| Validation: if config required, flat branches (no loop/if), break placement, commandOutcome scoping | `SequenceStepValidationService` tests |
| Contract round-trip (upsert → GET) incl. null vs empty `elseBody`; legacy sequences unaffected | service mapping tests |
| IfBlock render/edit/Add else/remove; shared ConditionFields; "Loops and Conditions" label; If button order | `src/web-ui/src/components/sequences/__tests__/IfBlock.test.tsx`, `SequencesPage.spec.tsx` |
