# Data Model: Use default label

No data change. No new entity. No API change. No storage change.

## Parameter row (UI only)

| Part | Source | Note |
|------|--------|------|
| Name | declaration name or label | unchanged |
| Box label | constant "Use default" | was "Inherit" |
| Box accessible name | `Use default <name>` | was `Inherit <name>` |
| Default text | `defaultDisplayText(declaration)` | see rules below |
| Box state | binding value is `INHERIT`, missing, or undefined | unchanged |

## Default text rules

| Default | Text |
|---------|------|
| null or undefined | "No default" |
| empty string | "(empty)" |
| choice | option label (fallback: raw value) |
| image | image id |
| number or text | raw value |

## Rules

- Set box: the binding stores `INHERIT` (no value in this place).
- Clear box: the binding stores a value for this place only.
- An empty-string default is a default. It does not show "No default".
- The default text is not part of the accessible name.
