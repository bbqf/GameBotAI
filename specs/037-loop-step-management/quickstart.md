# Quickstart: Sequence Loop Step Management

**Branch**: `037-loop-step-management` | **Date**: 2026-05-30

## Prerequisites

- Node.js ≥ 18
- Existing GameBot development environment set up

## Install New Dependency

From `src/web-ui/`:

```bash
npm install @dnd-kit/core @dnd-kit/sortable @dnd-kit/utilities
```

Expected additions to `package.json` `dependencies`:
- `@dnd-kit/core` ^6.x
- `@dnd-kit/sortable` ^8.x
- `@dnd-kit/utilities` ^3.x

## Run the Dev Server

```bash
cd src/web-ui
npm run dev
```

Open the app, navigate to a sequence, add a loop, then verify:
1. A persistent "Add step" button appears at the bottom of the step list
2. Clicking it adds a blank action step at the top level (outside the loop)
3. Steps can be dragged to reorder within their scope
4. Dragging a top-level step over the loop interior shows a "not allowed" indicator

## Run Tests

Unit tests:
```bash
cd src/web-ui
npm test
```

E2E tests (Playwright):
```bash
cd src/web-ui
npx playwright test
```

Key test files to create/modify:
- `src/web-ui/src/components/__tests__/SortableSequenceStepList.test.tsx` (new)
- `src/web-ui/src/components/sequences/__tests__/LoopBlock.test.tsx` (modify)
- `src/web-ui/tests/sequences-loop-steps.spec.ts` (new Playwright E2E)

## Key Files Changed

| File | Change |
|------|--------|
| `src/web-ui/src/components/SortableSequenceStepList.tsx` | New — DnD-based step list for sequences |
| `src/web-ui/src/components/SortableStepItem.tsx` | New — draggable wrapper for individual steps |
| `src/web-ui/src/components/sequences/LoopBlock.tsx` | Modified — replace ↑/↓ with DnD sortable body |
| `src/web-ui/src/pages/SequencesPage.tsx` | Modified — add DndContext, SortableSequenceStepList, bottom "Add step" button |
| `src/web-ui/src/index.css` (or equivalent) | Modified — add `.loop-block--drop-invalid` CSS class |
