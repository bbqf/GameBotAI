# Data Model: Semantic Actions UI

**Feature**: [specs/001-semantic-actions-ui/spec.md](specs/001-semantic-actions-ui/spec.md)  
**Date**: 2025-12-27

## Entities

### Action
- **id**: unique identifier
- **name**: display name shown in UI lists
- **type**: reference to ActionType key
- **attributes**: map of `key -> value` constrained by the selected ActionType’s AttributeDefinitions
- **validationStatus**: enum { valid, invalid }
- **validationMessages**: array of field-level issues (key, message)
- **createdBy / updatedBy**: audit fields
- **updatedAt**: timestamp for concurrency hints

### ActionType
- **key**: unique type identifier
- **displayName**: user-facing name
- **description**: short description of intent
- **version**: schema version for compatibility checks
- **attributeDefinitions**: ordered collection of AttributeDefinition entries

### AttributeDefinition
- **key**: machine-readable attribute name
- **label**: user-facing label
- **dataType**: enum { string, number, boolean, enum }
- **required**: boolean
- **constraints**: optional range (min, max), pattern (regex), allowedValues (for enum), defaultValue
- **helpText**: guidance shown in UI

### ValidationMessage
- **field**: attribute key or general
- **severity**: enum { error, warning }
- **message**: user-facing guidance

## Relationships
- `Action.type` references `ActionType.key`.
- `Action.attributes` keys must correspond to the selected `ActionType.attributeDefinitions.key` set.
- Validation results are derived from comparing `Action.attributes` against `AttributeDefinition` constraints.

## Derived/Behavioral Notes
- Switching `Action.type` triggers compatibility checks: attributes with matching keys and compatible data types are retained; others require discard confirmation.
- Attribute rendering is driven by `AttributeDefinition` (dataType → control; constraints → validation logic).
- Validation occurs client-side before save and must align with backend rules sourced from the authoritative schema.
