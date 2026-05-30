# Research: Images Authoring UI

## Decisions

### Image ID format and uniqueness
- **Decision**: Accept author-supplied IDs as case-sensitive strings limited to alphanumeric, dash, and underscore; must be unique across images.
- **Rationale**: Matches existing file-backed storage patterns, minimizes migration, and keeps IDs stable for trigger references.
- **Alternatives considered**: Auto-generated GUIDs (would break authoring control and existing references); case-insensitive IDs (risk of collisions on file system and unexpected matching rules).

### Allowed upload types and size enforcement
- **Decision**: Allow png, jpg, and jpeg content types; reject others; enforce 10 MB max payload (from spec) with clear validation errors.
- **Rationale**: These formats cover current templates and browser preview compatibility while aligning with size constraint already defined.
- **Alternatives considered**: Allow bmp/webp (adds conversion/preview variability); allow any binary (risks unsupported previews and security surface).

### Overwrite and delete response semantics
- **Decision**: Use PUT /api/images/{id} for overwrite with last-write-wins; return 409 Conflict for delete when trigger references exist, including referencing trigger IDs in the body.
- **Rationale**: PUT aligns with idempotent replacement; 409 cleanly signals dependency violation and is consistent with author guidance about blocking deletion when in use.
- **Alternatives considered**: POST to same ID (less clear for replacement semantics); 400/423 for delete conflicts (less specific to dependency state).
