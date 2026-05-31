# Data Model: Image Selector Dropdown

**Feature**: 045-image-selector-dropdown  
**Date**: 2026-05-30

## Existing Types (unchanged)

```typescript
// src/web-ui/src/services/images.ts (existing)
// listImages() → Promise<string[]>   — returns array of image ID strings
// getImageBlob(id: string) → Promise<Blob>
```

## New Types

### `ImageSelectorDropdownProps`

```typescript
// src/web-ui/src/components/images/ImageSelectorDropdown.tsx

type ImageSelectorDropdownProps = {
  id?: string;
  label?: string;
  value: string;                         // current image ID; empty string = unset
  onChange: (id: string) => void;        // called with '' when cleared
  required?: boolean;                    // true = no clear button (LOC-01 only)
  disabled?: boolean;
  error?: string;                        // external validation error message
  onStaleChange?: (isStale: boolean) => void; // fires when fetched list reveals value is stale
};
```

### `useImageList` return type

```typescript
// Internal hook in ImageSelectorDropdown.tsx

type UseImageListResult = {
  images: string[];
  loading: boolean;
  error: string | null;
  retry: () => void;
};

// Signature:
// function useImageList(active: boolean): UseImageListResult
// Fetches listImages() whenever active transitions false → true.
```

### `ImageThumbnailProps`

```typescript
// src/web-ui/src/components/images/ImageThumbnail.tsx

type ImageThumbnailProps = {
  imageId: string;
  className?: string;
  alt?: string;
};
// Fetches getImageBlob(imageId) on mount, creates an object URL, renders <img>.
// Shows a placeholder on load/error.
// Does not revoke object URLs — session-lifetime cache; safe at this scale.
```

## Module-Level Cache

```typescript
// src/web-ui/src/components/images/ImageThumbnail.tsx

// Session-lifetime cache: imageId → object URL string
// Prevents duplicate blob fetches across dropdown open/close cycles.
const thumbnailCache = new Map<string, string>();
```

## Validation Rules

| Field | Rule |
|-------|------|
| `value` (required=true, LOC-01) | Must be non-empty AND present in the fetched image list; otherwise `onStaleChange(true)` fires and the parent shows a validation error blocking save. |
| `value` (required=false, LOC-02–LOC-09) | May be empty string (cleared). If non-empty and not in list, shows stale warning but does not block save. |

## Affected Call Sites Summary

| Location | Current state variable type | `required` prop |
|----------|-----------------------------|-----------------|
| LOC-01 CommandForm primitive tap | `string` (`pendingPrimitiveReferenceImageId`) | `true` |
| LOC-02 CommandForm wait image | `string` (`pendingWaitReferenceImageId`) | `false` |
| LOC-03 CommandForm detection ref | `string \| undefined` (`value.detection?.referenceImageId`) — reads/writes form value prop directly, no local state | `false` |
| LOC-04 SequencesPage inline wait | `string` (inline state) | `false` |
| LOC-05 SequencesPage inline image-visible | `string` (inline state) | `false` |
| LOC-06 SequencesPage add step modal wait | `string` (`pendingWaitReferenceImageId`) | `false` |
| LOC-07 SequencesPage edit step wait | `string` (edit state) | `false` |
| LOC-08 LoopBlockHeader image-visible | `string` (condition.imageId) | `false` |
| LOC-09 BreakStepRow image-visible | `string` (breakCondition.imageId) | `false` |
