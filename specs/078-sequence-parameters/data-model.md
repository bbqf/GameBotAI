# Phase 1 Data Model: Sequence & Command Parameters

**Feature**: 078-sequence-parameters
**Date**: 2026-08-24

Every new field is **optional and additive**. Absent ⇒ pre-feature semantics (FR-032). No stored
JSON needs rewriting.

---

## 1. New domain types — `src/GameBot.Domain/Parameters/`

### `ParameterValueType` (enum)

```csharp
public enum ParameterValueType { Text, Number }
```

Serialized as its name (`"text"` / `"number"`, camel-cased by the existing JSON options).

### `ParameterDeclaration`

```csharp
public sealed class ParameterDeclaration {
  public required string Name { get; init; }
  public ParameterValueType Type { get; init; } = ParameterValueType.Text;
  public string? Default { get; init; }        // null = no default
  public bool Required { get; init; }
  public string? Description { get; init; }
}
```

**Validation rules** (FR-001, FR-003):

| Rule | Error |
|---|---|
| `Name` non-empty, matches `^[A-Za-z_]\w*$` | `parameter name '<n>' is not a valid identifier` |
| `Name` is not `iteration` | `parameter name 'iteration' is reserved` |
| `Name` does not start with `queue.` (and contains no `.`) | `parameter names may not use the reserved 'queue' namespace` |
| No two declarations on one entity share a name, **case-sensitively compared, and also rejected when they differ only by case** | `duplicate parameter name '<n>'` |
| When `Type == Number` and `Default` is set, `Default` parses as `int` (invariant culture) | `default value '<v>' for numeric parameter '<n>' is not a whole number` |
| `Required == true` and `Default != null` | allowed — the default simply makes it always satisfiable |

Ordering is positional (list order) and is preserved for display.

### `ParameterBinding`

```csharp
public sealed class ParameterBinding {
  public required string Name { get; init; }
  public string? Value { get; init; }   // null ⇒ inherit; "" ⇒ deliberate empty value
}
```

`null` vs `""` is load-bearing: `null` means "not bound here, keep walking outward",
`""` is a real value that satisfies resolution (spec Edge Cases).

### `ParameterScope`

```csharp
public sealed class ParameterScope {
  public static ParameterScope Empty { get; }
  public ParameterScope? Parent { get; }
  public string LayerName { get; }                 // "queue" | "entry" | "sequence" | "command" | "loop"
  public bool TryResolve(string name, out ParameterValue value);
  public ParameterScope Child(string layerName,
                              IEnumerable<ParameterBinding>? bindings,
                              IEnumerable<ParameterDeclaration>? declarations);
  public static ParameterScope FromQueue(ExecutionQueue queue);
  public ParameterScope WithIteration(int iteration);
  public IReadOnlyList<ScopeEntry> Describe();      // for the UI effective-value preview
}

public readonly record struct ParameterValue(string Text, string OriginLayer);
public sealed record ScopeEntry(string Name, string? Value, string OriginLayer, bool Declared);
```

**Resolution order** inside `TryResolve` (FR-009, innermost-first):

1. This layer's explicit bindings whose `Value != null`.
2. Walk to `Parent` and repeat.
3. If no layer supplied it, fall back to the **innermost declaration's** `Default`.
4. Not found ⇒ `false`; the caller raises the FR-017 error.

Immutable; `Child` allocates a new node and never mutates the parent (see research R2).

### `ParameterResolutionResult`

```csharp
public sealed record ParameterResolutionError(string ParameterName, string FieldPath, string Reason);
```

Two reasons only: `unresolved` (FR-017) and `not_a_number` (FR-019). Both surface as a step failure
message of the form
`Step '<stepId>': parameter '<name>' used by field '<fieldPath>' could not be resolved.`

### `ParameterNameRules` (static)

Reserved-name checks, the identifier regex, and the `queue.*` built-in catalogue, so the domain,
the API validators and the scope builder share one definition.

### `ParameterReferenceScanner` (static)

Walks a `Command` or a `CommandSequence` and returns every `{{name}}` reference it contains, paired
with a dotted field path. Used by save-time validation (FR-020), by the unused-value warning
(FR-012b), and by the "which names does this entity need" endpoint.

---

## 2. Changed existing types

### `Command` (`Commands/Command.cs`)

```csharp
public Collection<ParameterDeclaration> Parameters { get; init; } = new();   // NEW
```

Empty collection serializes as `[]`; absent in stored JSON deserializes to empty. No behaviour change
when empty.

### `CommandStep` (`Commands/CommandStep.cs`)

```csharp
public Dictionary<string, string>? FieldTemplates { get; init; }        // NEW — numeric-field overlay
public Collection<ParameterBinding>? ParameterBindings { get; init; }  // NEW — only for Type == Command
```

**`FieldTemplates` keys** — the complete supported set (research R3). Any other key is rejected at
save time so typos cannot silently no-op:

| Key | Target | Type |
|---|---|---|
| `swipe.startX`, `swipe.startY`, `swipe.endX`, `swipe.endY` | `SwipeConfig` | int |
| `swipe.durationMs` | `SwipeConfig` | int? |
| `waitForImage.timeoutMs` | `WaitForImageConfig` | int? |
| `ensureEmulatorRunning.instanceIndex` | `EnsureEmulatorRunningConfig` | int? |
| `ensureGameRunning.readinessTimeoutMs` | `EnsureGameRunningConfig` | int |
| `primitiveTap.detectionTarget.confidence` | `DetectionTarget` | double? |
| `primitiveTap.detectionTarget.offsetX`, `...offsetY` | `DetectionTarget` | int? |
| `ensureGameRunning.readinessImage.confidence` / `.offsetX` / `.offsetY` | `DetectionTarget` | numeric |

String-typed fields carry their placeholder **inline** and appear in no overlay:
`ensureEmulatorRunning.adbSerial`, `ensureEmulatorRunning.instanceName`, `keyInput.key`,
`primitiveTap.detectionTarget.referenceImageId`, `ensureGameRunning.readinessImage.referenceImageId`.

`CommandStep.TargetId` is **excluded** from substitution when `Type == Command` (it is a command
reference — FR-007).

### `CommandSequence` (`Commands/CommandSequence.cs`)

```csharp
public Collection<ParameterDeclaration> Parameters { get; init; } = new();   // NEW
```

Persisted alongside `steps`; follows the existing `[JsonInclude]` writable-collection pattern used by
`StepsWritable`.

### `SequenceStep` (`Commands/SequenceStep.cs`)

```csharp
public Collection<ParameterBinding>? ParameterBindings { get; set; }   // NEW — only for StepType == Command
```

Action-payload parameters need no new field: `SequenceActionPayload.Parameters` is
`Dictionary<string, object?>` and already accepts a placeholder string in a numeric slot
(research R3).

`ApplyScope` (renamed from `ApplyIterContext`) must copy this field through when it clones a step —
the current clone at SequenceRunner.cs:1308 drops anything it does not list, which is exactly the
kind of omission that would make bindings vanish inside loop bodies.

### `QueueTemplateEntry` (`QueueTemplates/QueueTemplateEntry.cs`)

```csharp
public Collection<ParameterBinding> ParameterValues { get; init; } = new();   // NEW
```

Holds both *declared* bindings (name matches a declaration on the referenced sequence) and *ad-hoc*
values (FR-012a). The two are distinguished at read time by comparing against the sequence's
declarations, not by a stored flag — so renaming a declaration reclassifies automatically and
surfaces as FR-023 / FR-012b feedback rather than as stale metadata.

Per-entry and independent: two entries referencing the same sequence hold separate collections
(FR-012), exactly as they already do for `Enabled` and the timer fields.

### `SelfRescheduleEntry` (`Services/QueueExecution/QueueRunHandle.cs`)

```csharp
internal sealed record SelfRescheduleEntry(..., ParameterScope? Scope);   // NEW trailing member
```

Carries the originating firing's scope so a re-fire resolves identically (FR-015).

---

## 3. Entity relationships

```
ExecutionQueue ──(built-ins)──────────────┐
   │ LinkedTemplateId                      │
   ▼                                       ▼
QueueTemplate ── Entries ─▶ QueueTemplateEntry ──ParameterValues──▶ ParameterScope("entry")
                                  │ SequenceId                             │
                                  ▼                                        ▼
                          CommandSequence ──Parameters──▶ declarations ──▶ ParameterScope("sequence")
                                  │ Steps                                  │
                                  ▼                                        ▼
                    SequenceStep(Command) ──ParameterBindings──────▶ ParameterScope("command")
                                  │ CommandId                              │
                                  ▼                                        ▼
                              Command ──Parameters──▶ declarations    resolved CommandStep
                                  │ Steps
                                  ▼
                             CommandStep ──FieldTemplates──▶ numeric overlay
```

A `Loop` step inserts one further ephemeral layer via `WithIteration(i)` for the duration of the
iteration.

---

## 4. Lifecycle / state

`ParameterScope` is **not persisted**. It exists from the moment
`QueueExecutionService.RunOneSequenceAsync` builds it until the firing completes. Declarations and
bindings are persisted (they are configuration); resolved values reach durable storage only as
execution-log detail (FR-024).

---

## 5. Persistence back-compatibility

| File | Pre-feature JSON | Post-feature read |
|---|---|---|
| `commands/*.json` | no `parameters`, no `fieldTemplates` | empty collection / null ⇒ identical behaviour |
| `sequences/*.json` | no `parameters`, no `parameterBindings` | empty collection / null ⇒ identical behaviour |
| `queue-templates/*.json` | no `parameterValues` | empty collection ⇒ identical behaviour |

Writers emit the new members only when non-empty, so a round-trip of an unparametrized entity
produces byte-identical JSON and no spurious diffs appear in the operator's authoring store.
