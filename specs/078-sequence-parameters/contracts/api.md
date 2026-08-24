# Phase 1 API Contracts: Sequence & Command Parameters

**Feature**: 078-sequence-parameters
**Base**: existing REST surface on `http://localhost:8080` (`GameBot.Service`)

Every field below is **optional on request** and **omitted from responses when empty**, so existing
clients and stored payloads are unaffected (FR-032). Contract snapshot tests under
`tests/contract/ApiContractSnapshots` must be regenerated to include the additive members.

---

## Shared shapes

```jsonc
// ParameterDeclarationDto
{
  "name": "adbSerial",          // required, ^[A-Za-z_]\w*$, not "iteration", no "queue." prefix
  "type": "text",               // "text" | "number"   (default "text")
  "default": "emulator-5558",   // optional; must parse as a whole number when type == "number"
  "required": false,            // default false
  "description": "ADB serial of the target emulator instance."
}

// ParameterBindingDto
{
  "name": "adbSerial",
  "value": "emulator-5560"      // null / omitted ⇒ inherit from the enclosing scope
}

// ParameterScopeEntryDto  (read-only, for UI previews and pickers)
{
  "name": "queue.emulatorSerial",
  "value": "emulator-5558",
  "originLayer": "queue",       // "queue" | "entry" | "sequence" | "command" | "default" | "loop"
  "declared": false,            // true when a declaration by this name exists on the entity
  "description": "The queue's bound ADB device serial."
}
```

---

## Commands

### `POST /api/commands` · `PUT /api/commands/{id}`

`CreateCommandRequest` / `UpdateCommandRequest` gain:

```jsonc
{
  "parameters": [ /* ParameterDeclarationDto */ ]
}
```

`CommandStepDto` gains:

```jsonc
{
  "fieldTemplates": { "swipe.startX": "{{originX}}" },   // numeric-field overlay, see data-model §2
  "parameterBindings": [ /* ParameterBindingDto */ ]     // only when "type": "Command"
}
```

**400 responses** (blocking, FR-003 / FR-020):

| Condition | `error` |
|---|---|
| Malformed / reserved / duplicate declaration name | `invalid_parameter_declaration` |
| Numeric default that is not a whole number | `invalid_parameter_default` |
| `fieldTemplates` key not in the supported set | `unknown_field_template_path` |
| Reference to a name that is neither declared, nor a `queue.*` built-in, nor `iteration` inside a loop | `unresolvable_parameter_reference` |
| `parameterBindings` naming a parameter the target command does not declare | `unknown_parameter_binding` |
| `{{...}}` in a command-reference field (`targetId` of a `Command` step) | `parameter_in_reference_field` |

Error bodies carry `details: [{ "fieldPath": "...", "parameterName": "..." }]` so the UI can anchor
the message inline (FR-029).

**200 with warnings** (non-blocking, FR-023a): body gains
`warnings: [{ "code": "static_check_skipped", "fieldPath": "primitiveTap.detectionTarget.referenceImageId" }]`.

### `GET /api/commands/{id}` → `CommandResponse`

Gains `parameters`, and per step `fieldTemplates` / `parameterBindings`.

### `GET /api/commands/{id}/parameter-scope` — NEW

Returns the names a picker may offer while editing this command (FR-026):

```jsonc
{ "entries": [ /* ParameterScopeEntryDto */ ] }
```

Contains this command's declarations plus the `queue.*` built-in catalogue. Values are `null`
because no run is in progress; `originLayer` still tells the UI where each would come from. Serving
this from the backend keeps one implementation of the resolution rules (research R9).

---

## Sequences

### `POST /api/sequences` · `PUT /api/sequences/{id}`

Request gains `parameters` (declarations). Each step of `stepType: "Command"` gains
`parameterBindings`.

**400 responses**: same table as Commands, plus `unknown_parameter_binding` when a step binds a name
the referenced command does not declare.

### `GET /api/sequences/{id}` → gains `parameters` and per-step `parameterBindings`.

### `GET /api/sequences/{id}/parameter-scope` — NEW

Same shape as the command variant. Additionally returns, for each `Command` step, the referenced
command's declarations so the binding form (FR-027) can render without N extra fetches:

```jsonc
{
  "entries": [ /* ParameterScopeEntryDto */ ],
  "stepCallees": [
    { "stepId": "s1", "commandId": "abc", "commandName": "Ensure game running",
      "parameters": [ /* ParameterDeclarationDto */ ] }
  ]
}
```

### `POST /api/sequences/{id}/execute` — CHANGED

Gains an optional body member for ad-hoc runs (FR-031):

```jsonc
{ "parameters": [ /* ParameterBindingDto */ ] }
```

**409 `missing_required_parameters`** when a declared `required` parameter has neither a supplied
value nor a default. Body lists `parameters: ["adbSerial"]`.

---

## Queue templates

### `POST /api/queue-templates` (save)

`TemplateEntrySaveRequest` gains:

```jsonc
{ "parameterValues": [ /* ParameterBindingDto */ ] }
```

Holds declared bindings **and** ad-hoc values (FR-012a). Ad-hoc names obey the same identifier and
reserved-name rules as declarations.

**200 with warnings** (never blocking):

```jsonc
{
  "warnings": [
    { "code": "unused_parameter_value", "entryIndex": 2, "name": "adbSeril" },      // FR-012b
    { "code": "stale_parameter_binding", "entryIndex": 0, "name": "waitMs" },       // FR-023
    { "code": "unsatisfied_required_parameter", "entryIndex": 1, "name": "gameId" } // FR-021
  ]
}
```

**400 `invalid_parameter_value_name`** only for a malformed or reserved name.

### `GET /api/queue-templates/{id}` → `QueueTemplateDetailResponse`

`QueueTemplateEntryResponse` gains:

```jsonc
{
  "parameterValues": [ /* ParameterBindingDto */ ],
  "hasParameterOverrides": true,                  // FR-030 — list badge without opening the entry
  "effectiveParameters": [ /* ParameterScopeEntryDto */ ]   // FR-028 — value + originLayer preview
}
```

`effectiveParameters` is computed against the queue currently linked to this template, when exactly
one is linked; otherwise `originLayer` is reported and `value` is `null` for queue-sourced names.

---

## Queues

### `POST /api/queues/{id}/start` — CHANGED

Pre-flight validation before any device work (FR-022). On failure:

**409 `missing_required_parameters`**

```jsonc
{
  "error": "missing_required_parameters",
  "entries": [
    { "entryIndex": 1, "sequenceId": "…", "sequenceName": "PNS Daily",
      "missing": ["adbSerial"] }
  ]
}
```

Only **enabled** entries are checked; disabled entries are ignored, consistent with feature 077.

---

## Execution logs

Step detail items gain a `resolvedParameters` member (FR-024), unredacted (spec Clarifications):

```jsonc
{
  "resolvedParameters": [
    { "name": "adbSerial", "value": "emulator-5558", "originLayer": "queue" }
  ]
}
```

Present only for steps that actually resolved at least one parameter, so existing log payloads and
their snapshot tests are unchanged for unparametrized runs.

---

## Error-message contract

Run-time failures (FR-017 / FR-019) use these exact forms so operators and tests can match on them:

```
Step '<stepId>': parameter '<name>' used by field '<fieldPath>' could not be resolved from any scope.
Step '<stepId>': parameter '<name>' resolved to '<value>', which is not a whole number for field '<fieldPath>'.
```
