# Data Model: Web UI Navigation Restructure

## Entities

### NavigationArea
- **Fields**: `id` (string; one of `authoring|execution|configuration`), `label` (string), `path` (string route), `order` (number), `isActive` (computed), `isVisible` (bool; Execution remains visible though empty).
- **Constraints**: Exactly three areas; `id` unique; `path` resolves to existing view; `label` non-empty.

### NavigationState
- **Fields**: `activeAreaId` (string; one of NavigationArea ids), `isCollapsed` (bool; true when viewport < ~768px), `focusTarget` (string|null for accessibility focus management).
- **Constraints**: `activeAreaId` required; `isCollapsed` derived from viewport; focus target must correspond to a rendered control when set.

### ExecutionPlaceholder
- **Fields**: `title` (string), `message` (string), `links` (array of {label, targetAreaId}).
- **Constraints**: Links, if present, must reference valid NavigationArea ids; message must explain future functionality (not empty).

## Relationships
- NavigationState references one active NavigationArea at any time.
- ExecutionPlaceholder links provide shortcuts back to other NavigationArea entries.

## Validation Rules
- Navigation areas count must remain three; adding/removing requires spec change.
- Collapsed menu must preserve readable labels and one-click activation.
- Legacy Trigger routes must redirect into Authoring without exposing removed UI.
