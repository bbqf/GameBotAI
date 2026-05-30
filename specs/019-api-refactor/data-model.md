# Data Model: API Structure Cleanup

## Entities

### API Resource Group
- **Purpose**: Logical domain grouping (Actions, Sequences, Sessions/Emulator, Configuration, Triggers/Detection).
- **Attributes**:
  - Name (string; canonical tag used in Swagger)
  - RoutePrefix (string; `/api/{resource}` form)
  - Description (string; shown in docs)
- **Relationships**: Owns a collection of Endpoint Catalog entries.

### Endpoint Catalog Entry
- **Purpose**: Defines a canonical route and documentation metadata.
- **Attributes**:
  - Method (string; GET/POST/PUT/DELETE)
  - Path (string; `/api/{resource}/...`)
  - Summary (string; domain-aligned wording)
  - RequestSchema (shape overview; references existing request DTOs)
  - ResponseSchema (shape overview; references existing response DTOs)
  - ExamplePayload (representative request/response example for Swagger)
  - Tags (list of strings; includes the owning resource group)
- **Constraints**:
  - Path must start with `/api/` and be unique within the catalog.
  - Each endpoint belongs to exactly one Resource Group for documentation/tagging.
- **Legacy Handling**:
  - LegacyPath (optional string; old non-`/api` path) mapped to a non-success outcome with guidance to the canonical path.

## Notes
- No new persistence is introduced; catalog information is realized in routing configuration and Swagger metadata.
- Existing DTOs remain authoritative for schemas; this model tracks how they are exposed and documented.
