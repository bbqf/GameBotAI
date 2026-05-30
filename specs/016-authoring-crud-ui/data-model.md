# Data Model: Authoring CRUD UI

**Branch**: 001-authoring-crud-ui  
**Date**: 2025-12-26

## Entities

### Action
- Fields: `id` (string, GUID), `name` (string, required, non-unique), `description` (string, optional)
- Relationships: Referenced by `Command`, `Trigger`
- Validation: `name` required; cannot delete if referenced

### Command
- Fields: `id` (string, GUID), `name` (string, required, non-unique), `parameters` (object/map, optional), `actions` (array of `Action.id`)
- Relationships: References `Action`; referenced by `Sequence` and potentially `Trigger`
- Validation: `name` required; at least 1 action optional (NEEDS CLARIFICATION in API contract)

### Game
- Fields: `id` (string, GUID), `name` (string, required, non-unique), `metadata` (object, optional)
- Relationships: Context for organizing objects
- Validation: `name` required

### Sequence
- Fields: `id` (string, GUID), `name` (string, required, non-unique), `steps` (array of `Command.id`, ordered)
- Relationships: References `Command`
- Validation: `name` required; steps can be empty; cannot delete if referenced (by triggers)

### Trigger
- Fields: `id` (string, GUID), `name` (string, required, non-unique), `criteria` (object), `actions` (array of `Action.id` optional), `commands` (array of `Command.id` optional), `sequence` (`Sequence.id` optional)
- Relationships: May reference `Action`, `Command`, or `Sequence`
- Validation: `name` required; cannot delete if referenced by runtime bindings

## General Rules
- Names are non-unique; UI disambiguates where needed.
- Deletion is blocked when an object is referenced; UI shows guidance.
- Dropdowns show names; values sent/stored as IDs.
