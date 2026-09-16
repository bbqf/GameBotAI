# Contract: no-information diagnostics

**Feature**: 090-fix-masked-low-variance | **Satisfies**: FR-017, SC-009

## Why

The fix turns a class of spurious `1.0` matches into no-match. An operator whose sequence "used to
match and now does not" needs to be able to tell the difference between *the target was absent* and
*the region was too featureless to judge*. Without that signal this fix becomes the next
hard-to-diagnose bug.

FR-013 freezes the response body, so the signal goes to the existing detection log channel.

## Domain

`TemplateMatchResult.NoInformationPositionCount` — `int`, init-only, domain only.

- Counts candidate positions scored zero by the no-information rule.
- `0` when the comparison was unmasked.
- `0` when the comparison was masked and every position was above the cutoff.
- Counts positions **suppressed**, never positions scored — the same naming discipline feature 089
  used for `retainedPixelCount` (a count of pixels kept, never of pixels masked out).

## Log event

Emitted from `ImageDetectionsEndpointComponent`, following the pattern established by 089's
`LogDetectMask`: a **new event id**, never a repurposed message format.

| | |
|---|---|
| Event id | `11006` |
| Level | `Information` |
| Message | `Detect no-information id={Id} suppressedPositions={SuppressedPositions} retainedPixelCount={RetainedPixelCount}` |
| Method | `LogDetectNoInformation(this ILogger logger, string Id, int SuppressedPositions, int RetainedPixelCount)` |

**Emitted only when `SuppressedPositions > 0`.** A detection over ordinary content logs nothing
extra, so the common path keeps its current log volume and the event's presence is itself the
signal.

## Response body

Unchanged. `masked` and `retainedPixelCount` keep their current meanings and are the only
mask-related fields on the wire. `NoInformationPositionCount` does **not** appear in any DTO, and
the contract tests assert its absence so a later change cannot add it by accident.
