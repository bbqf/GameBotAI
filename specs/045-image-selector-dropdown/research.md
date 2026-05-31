# Research: Image Selector Dropdown

**Feature**: 045-image-selector-dropdown  
**Date**: 2026-05-30

## Decision 1: Component Architecture

**Decision**: Build a new custom `ImageSelectorDropdown` component rather than extending `SearchableDropdown`.

**Rationale**: `SearchableDropdown` renders a native HTML `<select>` element, which does not support custom option rendering. Thumbnails require rendering `<img>` elements inside each list item. A custom popover-style dropdown (trigger button + panel) is needed.

**Alternatives considered**:
- Extend `SearchableDropdown` — rejected because native `<select>` options cannot embed images.
- Repurpose `ImagePicker.tsx` — `ImagePicker` is a section-level widget, not a form control. It fetches on mount (not on open), has no trigger button, and lacks the required/optional/error contract. It is not suitable as a drop-in replacement for text inputs.
- Third-party library (react-select, etc.) — rejected to avoid adding dependencies to a project that deliberately builds custom components.

---

## Decision 2: Thumbnail Fetching Strategy

**Decision**: Lazy-fetch each thumbnail blob via `getImageBlob(id)` when the list item first renders. Cache object URLs in a module-level `Map<string, string>` for the session lifetime to avoid duplicate fetches during the same page session.

**Rationale**: The `/api/images/{id}` endpoint requires authentication headers, so a plain `<img src="/api/images/{id}">` tag will not work without custom fetch. Fetching lazily (on render) ensures images that are never scrolled into view are never fetched. The module-level cache ensures re-opening the dropdown does not re-fetch thumbnails already loaded.

**Alternatives considered**:
- Fetch all thumbnails immediately when the dropdown opens — rejected for performance with large image libraries (50+ fetches before the user sees anything).
- Per-component `useState` cache — rejected; object URLs would be re-fetched on every open.
- `IntersectionObserver` for true lazy loading — deferred as over-engineering for the current scale (≤50 images); simple render-on-mount per item is sufficient.

---

## Decision 3: Image List Fetch Timing

**Decision**: Fetch the image list (via `listImages()`) each time the dropdown transitions from closed to open. Do not cache the list across opens.

**Rationale**: Spec (FR-002, clarification Q3) requires the dropdown always reflects the current state of the library. Fetching on every open ensures a newly captured image is immediately available without a page reload. The list is lightweight (array of strings), so the fetch is fast.

**Alternatives considered**:
- Fetch once on page mount — rejected per spec clarification (must be always-fresh).
- Stale-while-revalidate — rejected as unnecessary complexity for an authoring tool with low image change frequency.

---

## Decision 4: Hook Structure

**Decision**: Extract a `useImageList` hook that accepts an `active: boolean` trigger and returns `{ images, loading, error, retry }`. The component calls this hook and passes its `open` state as `active`.

**Rationale**: Separates data-fetching from rendering. Makes the hook independently testable. Follows the established pattern in the codebase (e.g., `useGames`).

---

## Decision 5: File Location

**Decision**: Place new files in `src/web-ui/src/components/images/`:
- `ImageSelectorDropdown.tsx` — main component + `useImageList` hook
- `ImageThumbnail.tsx` — reusable thumbnail sub-component with blob fetching

**Rationale**: `src/web-ui/src/components/images/` already contains `EmulatorCaptureCropper.tsx`. Image-specific components belong there. `ImageSelectorDropdown` and `ImageThumbnail` are image-domain components, not generic form controls.

---

## Decision 6: Value Contract

**Decision**: `ImageSelectorDropdown` uses `value: string` and `onChange: (id: string) => void`. An empty string represents "no selection" (unset). Required fields (`required={true}`) omit the clear button; optional fields include it and call `onChange('')` when cleared.

**Rationale**: Existing state variables in `CommandForm.tsx` and `SequencesPage.tsx` use `string` (empty string for unset). Using the same type avoids changing state types across many call sites.

---

## Decision 7: Stale Image Validation

**Decision**: When `value` is a non-empty string that is not present in the fetched image list, the selector displays the stale ID with a warning indicator. For the required field (LOC-01, primitive tap), the parent `CommandForm` already has a `disabled` submit condition — the selector will expose an `error` prop; the parent passes an error when the value is stale, which blocks the form via existing validation patterns.

**Rationale**: Stale detection happens at the point of form render (after the list loads). The component reports stale status via a callback prop `onStaleChange?: (isStale: boolean) => void` so parents can wire validation without the component imposing form-level behavior.

---

## Performance Goals

| Metric | Target |
|--------|--------|
| Dropdown open → list visible | < 500 ms p95 (network-bound; list endpoint is lightweight) |
| Search filter response | < 50 ms (client-side string filter, no server round-trip) |
| Thumbnail display per item | Best-effort async; list is usable before thumbnails load |
