# Contract: Sequence step shapes

A step has `action.type` and `action.parameters`. Region = `{ "x": int, "y": int, "width": int, "height": int }`
in captured-screen pixels (same as `ocrOffset`).

## quiz-lookup

```json
{
  "type": "quiz-lookup",
  "parameters": {
    "game": "pns",
    "category": "radio",
    "store": "pns-quiz",
    "question": { "x": 40, "y": 200, "width": 400, "height": 90 },
    "answers": [
      { "x": 40, "y": 320, "width": 400, "height": 60 },
      { "x": 40, "y": 400, "width": 400, "height": 60 },
      { "x": 40, "y": 480, "width": 400, "height": 60 }
    ],
    "similarity": 0.85
  }
}
```

Outcome: `pick_1` .. `pick_N` | `unreadable` | `service_unavailable`.
`unreadable`: a question or answer region has empty or white-space text after normalization, or the
OCR engine fails. No pick is made. The game match is exact; the category match ignores case.
The run log says `known` or `guess`, the normalized question, and the picked position.

## quiz-learn

```json
{
  "type": "quiz-learn",
  "parameters": {
    "lookupStepId": "lookup-1",
    "correctPosition": 2
  }
}
```

Or use `"correctRegion": { ... }` in place of `correctPosition`.

Outcome: `stored` | `skipped_no_match` | `skipped_ambiguous` | `service_unavailable`.
The correct text must reach the similarity limit of the lookup (default 0.85) for exactly one answer.
None gives `skipped_no_match`; more than one gives `skipped_ambiguous`. No lookup result (the lookup
did not run) gives `skipped_no_match`, and the log says why.

## Validation (save time) -> HTTP 400, never 500

- `game`, `category`, `store` missing or blank.
- `answers` has fewer than two regions, or a region has width or height of 0 or less.
- `similarity` outside 0 to 1.
- `quiz-learn` without `lookupStepId`, or the id does not name a `quiz-lookup` step before the learn step.
- `quiz-learn` with both or neither of `correctRegion` and `correctPosition`.
- `correctPosition` below 1 (the run gives `skipped_no_match` if it is above N).

## API docs and editor

The `PayloadDescriptions` entry of each step holds the JSON above as an example step. The web UI
sequence editor lists both types and shows a basic field form for each input.

## Use in a sequence

A later `commandOutcome` condition reads the outcome, for example "step lookup-1 outcome equals pick_2".
The author then taps the answer at that position.
