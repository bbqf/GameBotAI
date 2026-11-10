# PR Notes: Feature 185

These notes hold the results that tasks T001, T042, T043, and T044 ask the author to put in the PR text.

## T001: start state

- `npm --prefix src/web-ui run test`: 199 suites, 1469 tests passed before any change.
- `npm --prefix src/web-ui run lint`: 0 errors, 1 old warning (`TargetForm.tsx`).

## Final state

- Jest: 209 suites, 1573 tests passed.
- Lint: 0 errors, the same old warning.
- Playwright: `editor-row-height` (4 tests) and `phone-entry-page` (7 sizes) passed. A closed row is 50 px high at 1024 px. The limit is 57 px.

## T043: shard rule

The web UI tests run as one CI job (`web-ui-tests`). They are not split into shards, so the shard balance rule does not apply.

## T044: mobile checklist for the entry page

| Line | Result | Evidence |
|------|--------|----------|
| The feature states its phone support level | Pass | The values are `full`. The schedule editor is `desktop-only`. See `docs/mobile-ui-principles.md`. |
| The level is set in `navigation.ts` | Pass | Not an area. Queues stays `reduced`. |
| A `desktop-only` area shows the notice | Pass | No new area. |
| One primary action | Pass | **Save**. |
| Each control is at least 44 x 44 px | Pass | Playwright, seven sizes. A checkbox is 24 px with a 10 px margin, as in the rest of the phone UI. |
| Each text input has a font of at least 16 px | Pass | Playwright, seven sizes. |
| Lists use cards, not tables | Pass | Each parameter row is a card. |
| No hover-only control | Pass | The new CSS has no `:hover` rule. |
| No sideways scroll at 320 to 768 px | Pass | Playwright, six widths. |
| Works in landscape (640 x 360) | Pass | Playwright. |
| Safe areas do not cut content | Not checked on a device | The page uses the same phone frame as the queue detail. |
| The connection-lost banner shows and the controls turn off | Pass | The banner is part of the app frame. A unit test checks that Save is off. |
| No action is sent again after the connection returns | Pass | A unit test checks it. |
| The desktop layout at 769 px and wider is unchanged | Pass, with one change by design | Only the entry card of the template editor changes. |
| All new text is STE | Pass | Checked with a script for sentences over 25 words. The artifacts were fixed. |

## T042: manual check

- Done: screenshots of the big screen (1024 px, open panel) and the phone (375 px: detail, entry page, dialog) in a real browser with mocked API routes. The layout is as designed.
- Not done: a check with the real service, a real phone, and a stop watch (SC-003, under 30 seconds). Do the steps of `quickstart.md` on a real phone before the release.
