# Implementation Plan: Image Selector Dropdown

**Branch**: `045-image-selector-dropdown` | **Date**: 2026-05-30 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/045-image-selector-dropdown/spec.md`

## Summary

Replace all 9 free-text image ID input fields across the authoring UI with a reusable `ImageSelectorDropdown` component that shows a searchable dropdown of available images, each with a miniature thumbnail. The component fetches the image library fresh on each open, supports required/optional semantics, and handles stale references. Two sub-artifacts are introduced: an `ImageThumbnail` component for lazy blob fetching, and a `useImageList` hook for data fetching with loading/error/retry.

## Technical Context

**Language/Version**: TypeScript 5.6.3 / React 18.3.1  
**Primary Dependencies**: React, Vite 7.3.2, custom component library (no external UI library)  
**Storage**: N/A (UI-only change; existing `/api/images` endpoints unchanged)  
**Testing**: Jest 29 + React Testing Library 14 + Playwright 1.49 (E2E)  
**Target Platform**: Web browser (modern; same as existing app)  
**Project Type**: Web application (frontend only for this feature)  
**Performance Goals**: Dropdown open → list visible <500ms p95; search filter response <50ms; thumbnails load async (non-blocking)  
**Constraints**: Auth headers required for image blob fetches (no plain `<img src>` without fetch wrapper); native `<select>` cannot embed thumbnails  
**Scale/Scope**: Image libraries up to 50 images; 9 call sites across 4 source files

## Constitution Check

| Gate | Status | Notes |
|------|--------|-------|
| Lint/format clean | Must pass | ESLint + project formatter; enforced in CI |
| No underscores in method names — CamelCase only | Must pass | All new functions/methods use CamelCase |
| Functions ≤50 LOC | Must pass | Component split into `ImageSelectorDropdown`, `ImageThumbnail`, `useImageList` to stay within limit |
| Unit tests ≥80% line, ≥70% branch coverage | Must pass | Tests planned for all new components and the hook |
| Performance goals declared | ✅ Declared above | |
| Actionable error messages | Must pass | Error states include "retry" action (FR-012) |
| Public API/component props documented | Must pass | Props types documented in data-model.md and inline |
| No new external dependencies | ✅ None added | |

No constitution violations. No waivers required.

## Project Structure

### Documentation (this feature)

```text
specs/045-image-selector-dropdown/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (new files)

```text
src/web-ui/src/components/images/
├── ImageSelectorDropdown.tsx    # NEW — main component + useImageList hook
├── ImageSelectorDropdown.css    # NEW — dropdown panel, trigger, thumbnail styles
├── ImageThumbnail.tsx           # NEW — lazy blob-fetch thumbnail component
└── __tests__/
    ├── ImageSelectorDropdown.test.tsx  # NEW — unit tests
    └── ImageThumbnail.test.tsx         # NEW — unit tests
```

### Source Code (modified files)

```text
src/web-ui/src/components/commands/CommandForm.tsx
  └── Replace LOC-01, LOC-02, LOC-03 text inputs with ImageSelectorDropdown

src/web-ui/src/components/sequences/BreakStepRow.tsx
  └── Replace LOC-09 text input with ImageSelectorDropdown

src/web-ui/src/components/sequences/LoopBlockHeader.tsx
  └── Replace LOC-08 text input with ImageSelectorDropdown

src/web-ui/src/pages/SequencesPage.tsx
  └── Replace LOC-04, LOC-05, LOC-06, LOC-07 text inputs with ImageSelectorDropdown
```

**Structure Decision**: Web application layout. New components go in `src/web-ui/src/components/images/` following the existing image-component subdirectory convention. No backend changes.

## Implementation Design

### `ImageThumbnail` Component

Located at `src/web-ui/src/components/images/ImageThumbnail.tsx`.

- Props: `imageId: string`, `className?: string`, `alt?: string`
- On mount: checks `thumbnailCache` (module-level `Map<string, string>`); if hit, uses cached URL; if miss, calls `getImageBlob(imageId)`, creates object URL, stores in cache, sets `src`
- Renders `<img>` when URL is available; renders a placeholder `<span>` icon during load or on error
- Does not revoke cached object URLs (session-lifetime cache); safe for the small scale of this app
- On error: shows placeholder silently (thumbnail failure is non-blocking per spec)

### `useImageList` Hook

Located inside `ImageSelectorDropdown.tsx`.

- Signature: `function useImageList(active: boolean): { images: string[], loading: boolean, error: string | null, retry: () => void }`
- Effect fires when `active` transitions to `true`; calls `listImages()` from `src/web-ui/src/services/images.ts`
- On success: sets `images`, clears `loading` and `error`
- On failure: sets `error` string, clears `loading`
- `retry()` increments an internal counter to re-trigger the effect
- Does not cache list between opens (always-fresh requirement)

### `ImageSelectorDropdown` Component

Located at `src/web-ui/src/components/images/ImageSelectorDropdown.tsx`.

**Props** (see data-model.md for full type):
- `value: string` — current image ID (empty = unset)
- `onChange: (id: string) => void` — fires on selection or clear
- `required?: boolean` — if true, no clear button
- `onStaleChange?: (isStale: boolean) => void` — fires when loaded list reveals value is stale
- `disabled?`, `error?`, `label?`, `id?`

**Render structure**:
```
<div class="image-selector">
  <label>          (if label prop)
  <button          (trigger: shows thumbnail + ID of selected, or placeholder)
    onClick → toggle open
  >
  {open && (
    <div class="image-selector__panel">
      <input type="text" placeholder="Search…" value={query} onChange={setQuery} />
      {loading && <div class="image-selector__loading">Loading…</div>}
      {error  && <div class="image-selector__error">{error} <button onClick={retry}>Retry</button></div>}
      {!loading && !error && images.length === 0 && <div>No images available</div>}
      {!loading && !error && filtered.map(id => (
        <button key={id} class="image-selector__option" onClick={() => { onChange(id); setOpen(false); }}>
          <ImageThumbnail imageId={id} />
          <span>{id}</span>
        </button>
      ))}
      {!required && value && (
        <button class="image-selector__clear" onClick={() => onChange('')}>Clear</button>
      )}
    </div>
  )}
  {staleWarning && <div class="field-warning">Image "{value}" not found in library</div>}
  {error prop && <div class="field-error" role="alert">{error}</div>}
</div>
```

**Stale detection**: After `images` loads, if `value` is non-empty and not in `images`, call `onStaleChange(true)`. When value is in the list or empty, call `onStaleChange(false)`.

**Close on outside click**: `useEffect` attaches a document `mousedown` listener when open; removes it on close or unmount.

### `CommandForm.tsx` Changes (LOC-01, LOC-02, LOC-03)

- **LOC-01** (`pendingPrimitiveReferenceImageId`): Replace `<input>` with `<ImageSelectorDropdown required value={...} onChange={setPendingPrimitiveReferenceImageId} onStaleChange={(s) => setPrimitiveTapStale(s)} error={primitiveTapStale ? 'Selected image no longer exists' : undefined} />`. Add `primitiveTapStale` state; include it in the submit disabled condition.
- **LOC-02** (`pendingWaitReferenceImageId`): Replace `<input>` with `<ImageSelectorDropdown value={...} onChange={setPendingWaitReferenceImageId} />`.
- **LOC-03**: Replace `<input>` with `<ImageSelectorDropdown value={value.detection?.referenceImageId ?? ''} onChange={(id) => onChange({ ...value, detection: { ...(value.detection ?? {}), referenceImageId: id } })} disabled={submitting} />`. Keep existing "leave blank to skip" hint text. Note: reads/writes via the form's `value`/`onChange` props directly — no local state variable; disabled by `submitting` only.

### `BreakStepRow.tsx` Changes (LOC-09)

Replace the `<input type="text" data-testid="break-image-id" ...>` with `<ImageSelectorDropdown id="break-image-id" value={breakCondition.imageId} onChange={(id) => onChange({ ...breakCondition, imageId: id })} disabled={disabled} />`.

### `LoopBlockHeader.tsx` Changes (LOC-08)

Replace the `<input data-testid="loop-condition-imageId" ...>` with `<ImageSelectorDropdown value={condition.imageId} onChange={(id) => onConditionChange({ ...condition, imageId: id })} disabled={disabled} />`.

### `SequencesPage.tsx` Changes (LOC-04, LOC-05, LOC-06, LOC-07)

Each of the four inline/modal image text inputs replaced with `<ImageSelectorDropdown value={...} onChange={...} />` following the same pattern. No state type changes required (all remain `string`).

## Contracts

This is an internal UI-only change. No external API contracts are affected. The existing `/api/images` REST endpoints are consumed but not changed.

The `ImageSelectorDropdown` component contract is the public interface within the frontend codebase — documented in `data-model.md`.

## Testing Strategy

### Unit Tests — `ImageSelectorDropdown.test.tsx`

Mock `listImages` from `src/web-ui/src/services/images.ts` using `jest.mock()`.

| Test | Scenario |
|------|----------|
| Renders trigger button with placeholder when value is empty | Initial state |
| Renders trigger button with selected image ID when value is set | Preselected value |
| Opens panel on trigger click | Open state |
| Shows loading indicator while fetching | Async loading |
| Shows image list after fetch resolves | Success path |
| Filters list on search input | Search filtering |
| Calls onChange with image ID on option click and closes panel | Selection |
| Shows empty state when library returns empty array | Empty state |
| Shows error message and retry button on fetch failure | Error state |
| Retries fetch when retry button clicked | Retry flow |
| Shows clear button for optional field; calls onChange('') | Clear optional |
| Hides clear button for required field | Required field |
| Fires onStaleChange(true) when value not in loaded list | Stale detection |
| Shows stale warning text | Stale display |
| Shows external error prop as field-error | Validation error |
| Closes on outside click | Focus management |

### Unit Tests — `ImageThumbnail.test.tsx`

Mock `getImageBlob`.

| Test | Scenario |
|------|----------|
| Renders img with object URL after blob fetches | Happy path |
| Renders placeholder during loading | Loading state |
| Renders placeholder on fetch error | Error fallback |
| Uses cached URL on second render of same ID | Cache hit |

### Integration / Regression

- Existing `CommandForm` tests: verify form submission still works after replacing inputs.
- Existing `BreakStepRow.test.tsx`: verify break condition editing still works.
- Manual test: open CommandForm → primitive tap step → selector shows images with thumbnails → select image → form submits.

## Complexity Tracking

No constitution violations requiring justification.
