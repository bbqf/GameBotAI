# Data Model: Reject unknown command step fields

No stored data changes. No DTO changes.

## Scanner (new, in memory only)

| Item | Description |
|------|-------------|
| Input | A `JsonElement` (request body root) and a DTO `Type` |
| Output | An ordered list of text paths, one for each unknown field. Empty list means valid |
| Type map | Per DTO type: set of public property names (OrdinalIgnoreCase), minus `[JsonIgnore]` properties, plus the property type for recursion |
| Recursion | Object property of DTO type: recurse. Collection property: recurse for each item, path `name[i]`. Scalar, enum, string, `Dictionary<string,string>`: stop |

## DTO types walked

- `CreateCommandRequest`, `UpdateCommandRequest`
- `CommandStepDto`
- `PrimitiveTapConfigDto`, `WaitForImageConfigDto`, `KeyInputConfigDto`, `SwipeConfigDto`,
  `EnsureEmulatorRunningConfigDto`, `EnsureGameRunningConfigDto`
- `DetectionTargetDto`, `PixelRegionDto`
- `ParameterDeclarationDto`, `ParameterBindingDto`

## Rules

- Unknown name (any value, including `null`) adds one path.
- A wrong value type is not the scanner's job. Existing deserialization errors stay.
- Order of paths follows the order in the body.
