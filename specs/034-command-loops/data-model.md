# Data Model: Command Loop Structures

**Feature**: 033-command-loops  
**Date**: 2026-03-31  
**Source**: research.md R-001 through R-007

---

## Domain Model Changes

### 1. `SequenceStepType` — extended enum

```csharp
// File: src/GameBot.Domain/Commands/SequenceStep.cs
public enum SequenceStepType
{
    Command,
    Action,
    Conditional,
    Loop,   // NEW
    Break,  // NEW
}
```

---

### 2. `SequenceStep` — new properties

```csharp
// File: src/GameBot.Domain/Commands/SequenceStep.cs (additions)
public class SequenceStep
{
    // ... existing fields unchanged ...

    /// Populated when StepType == Loop. Null otherwise.
    public LoopConfig? Loop { get; set; }

    /// Populated when StepType == Loop. Inner steps of the loop body.
    public Collection<SequenceStep> Body { get; init; } = new();

    /// Populated when StepType == Break. Null = unconditional break.
    public SequenceStepCondition? BreakCondition { get; set; }
}
```

---

### 3. `LoopConfig` — polymorphic hierarchy

```csharp
// File: src/GameBot.Domain/Commands/LoopConfig.cs (NEW)

[JsonPolymorphic(TypeDiscriminatorPropertyName = "loopType")]
[JsonDerivedType(typeof(CountLoopConfig),       typeDiscriminator: "count")]
[JsonDerivedType(typeof(WhileLoopConfig),        typeDiscriminator: "while")]
[JsonDerivedType(typeof(RepeatUntilLoopConfig),  typeDiscriminator: "repeatUntil")]
public abstract class LoopConfig
{
    /// Per-loop override for safety iteration limit.
    /// When null, the global config value (default 1000) is used.
    public int? MaxIterations { get; set; }
}

public sealed class CountLoopConfig : LoopConfig
{
    /// Number of iterations. Must be >= 0. Body is skipped when Count == 0.
    public int Count { get; set; }
}

public sealed class WhileLoopConfig : LoopConfig
{
    /// Condition evaluated before each iteration.
    /// Only imageVisible and commandOutcome are valid.
    public required SequenceStepCondition Condition { get; set; }
}

public sealed class RepeatUntilLoopConfig : LoopConfig
{
    /// Exit condition evaluated after each iteration.
    /// Only imageVisible and commandOutcome are valid.
    public required SequenceStepCondition Condition { get; set; }
}
```

---

### 4. `TemplateSubstitutor` — new utility

```csharp
// File: src/GameBot.Domain/Utils/TemplateSubstitutor.cs (NEW)

/// Replaces {{key}} placeholders in string values using a provided context dictionary.
/// Non-string JSON values are returned unchanged.
public static class TemplateSubstitutor
{
    private static readonly Regex Pattern =
        new(@"\{\{(\w+)\}\}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public static string Substitute(string template,
        IReadOnlyDictionary<string, string> context)
    {
        return Pattern.Replace(template, m =>
            context.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    /// Walk all string-typed parameter values in a SequenceActionPayload
    /// and substitute context variables.
    public static SequenceActionPayload SubstitutePayload(
        SequenceActionPayload payload,
        IReadOnlyDictionary<string, string> context)
    {
        // Deep-clone parameters and substitute string values in-place
        // Implementation delegates to Substitute() per string-typed entry.
    }
}
```

**Context dictionary for loops**: `{ "iteration": "1" }` (1-based, string representation). Injected by the loop runner at each iteration.

---

### 5. Execution Log — new types

```csharp
// File: src/GameBot.Domain/Logging/ExecutionLogModels.cs (additions)

/// Per-iteration outcome for a loop step.
public sealed record LoopIterationOutcome(
    int IterationIndex,
    bool BreakTriggered,
    IReadOnlyList<ExecutionStepOutcome> StepOutcomes
);

// Extension to ExecutionStepOutcome:
public sealed record ExecutionStepOutcome(
    int StepOrder,
    string StepType,
    string Outcome,
    string? ReasonCode,
    string? ReasonText,
    string? SequenceId,
    string? StepId,
    string? SequenceLabel,
    string? StepLabel,
    ConditionEvaluationTrace? ConditionTrace,
    IReadOnlyList<LoopIterationOutcome>? LoopIterations   // NEW — null for non-loop steps
);
```

---

### 6. Config — new global setting

```json
// data/config/config.json addition:
{
  "loopMaxIterations": 1000
}
```

Accessed via existing `IConfigRepository` / `AppConfig` model. Added as:

```csharp
// File: src/GameBot.Domain/Config/AppConfig.cs (addition)
public int LoopMaxIterations { get; set; } = 1000;
```

---

## Validation Rules

| Rule | When | Error |
|------|------|-------|
| `CountLoopConfig.Count` must be ≥ 0 | Save / validate | `loop.count must be a non-negative integer` |
| `LoopConfig.MaxIterations`, if set, must be > 0 | Save / validate | `loop.maxIterations must be a positive integer` |
| Loop body MUST NOT contain `StepType == Loop` steps | Save / validate | `nested loops are not supported in v1` |
| `SequenceStepType.Break` MUST only appear inside a loop body | Save / validate | `break step is not valid outside a loop body` |
| `{{iteration}}` placeholder MUST only appear inside a loop body's step parameters | Save / validate | `{{iteration}} placeholder is only valid inside a loop body` |
| `LoopConfig.Condition` (while/repeatUntil) evaluation error at runtime | Runtime | Loop step outcome = `failed`; command stops |
| `commandOutcome` condition in a loop MUST reference a step that precedes the loop or a step earlier in the current loop body only (no forward refs) | Save / validate | `commandOutcome stepRef references a step that has not yet executed` |

---

## Entity Relationships

```
Sequence
  └── SequenceStep (StepType: Action | Command | Conditional | Loop | Break)
        └── [StepType == Loop]
              ├── LoopConfig  (loopType: count | while | repeatUntil)
              │     └── [while / repeatUntil] WhileLoopConfig / RepeatUntilLoopConfig
              │           └── Condition: SequenceStepCondition  (imageVisible | commandOutcome)
              └── Body: Collection<SequenceStep>  (same types EXCEPT Loop)
                    └── [StepType == Break]
                          └── BreakCondition: SequenceStepCondition?  (null = unconditional)
```

---

## State Transitions (Runtime)

### Count Loop
```
Enter → i = 1
  Check i <= Count → No → Exit loop (success)
  Execute body → Break signal? → Yes → Exit loop (success, break)
  i++ → repeat
  i > MaxIterations → Exit loop (failure, limit)
```

### While Loop
```
Enter
  Evaluate condition → Error → Exit loop (failure, conditionError)
  Evaluate condition → False → Exit loop (success)
  i++; Execute body → Break signal? → Yes → Exit loop (success, break)
  i > MaxIterations → Exit loop (failure, limit)
  repeat
```

### Repeat-Until Loop
```
Enter → i = 1
  Execute body → Break signal? → Yes → Exit loop (success, break)
  i > MaxIterations → Exit loop (failure, limit)
  Evaluate condition → Error → Exit loop (failure, conditionError)
  Evaluate condition → True → Exit loop (success)
  i++ → repeat
```
