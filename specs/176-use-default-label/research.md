# Research: Use default label

## Decision 1: Change only visible text and accessible name

- Decision: Change the label span to "Use default". Change the `aria-label` to `Use default ${name}`. Keep the `INHERIT` sentinel, the CSS class `parameter-binding-inherit`, and the state variable names.
- Rationale: FR-006 and FR-007 forbid a behaviour change. A rename of the stored value would risk the resolve rules (feature 078).
- Alternatives: Rename the sentinel and class. Rejected: larger diff, no user gain.

## Decision 2: Source of the default text

- Decision: Use `declaration.default` (already used for the "Falls back to the declared default" line). If `default` is null or undefined, show "No default". An empty string is a default and shows the marker "(empty)".
- Rationale: Matches the spec edge case and FR-003. The field already exists in the declaration type.
- Alternatives: Use the `effective` resolved value. Rejected: that is the value from any layer, not the default.

## Decision 3: Display of non-text defaults

- Decision: No helper exists today. Add `defaultDisplayText(declaration)` in `components/parameters/types.ts`. Rules:
  - null or undefined: "No default".
  - empty string: "(empty)".
  - choice: the option label (fallback: raw value).
  - image: the image id as text.
  - number and text: the raw value.
- Rationale: Clarifications of 2026-11-03 (loop 1 and loop 2). The choice field shows `option.label`; the other fields show the raw value.
- Tests: choice, number, image, empty, null, and undefined defaults (FR-003).

## Decision 4: Layout on a phone and stylesheet

- Decision: Put the default text in a `span` with class `parameter-binding-default` in the same row. Add its rule to `src/web-ui/src/styles.css`. That file holds the other `.parameter-*` rules. The rule uses `overflow-wrap: anywhere` and the muted color variable.
- Rationale: FR-009. A long default must not break the row. The class `parameter-binding-inherit` has no CSS rule, so a new rule is needed.
- Check: narrowest phone width in docs/mobile-ui-principles.md, no clipped text, 44 px touch target.

## Decision 5: Docs

- Decision: Matches of "inherit" in docs/ (`parameters.md`, `architecture.md`) may use the word as a verb for the resolve rules, not as the box label. Recheck with a search at implementation time. Change only a match that names the box (FR-008).
- Alternatives: None.

## Decision 6: Ad-hoc and preview rows

- Decision: No change to behaviour. Ad-hoc rows and preview mode have no box. In `ParameterFormPreview.tsx` only the comments change (FR-013).

## Decision 7: Release items

- Decision: Add a CHANGELOG entry in STE. Add the row for 176 in `specs/STATUS.md` with the same format as the row for 175. Bump the version at push time: `patch` on `origin/master` plus 1 (FR-010, FR-011, FR-012).
- Rationale: The change is user-visible and the project rules need these items.
