# Research: Gamepacks Page Take-Over Option

## D1. The page has no dialogs

- Finding: `GamepacksPage.tsx` shows a list of catalog versions. Each version with an update has one button. A page-level
  `Dry run` check box controls the call. There is no dialog.
- Decision: Put the take-over check box next to the `Dry run` check box. It applies to all buttons on the page.
- Rationale: It is the smallest change. It keeps the existing layout and phone CSS.
- Alternatives: Add a dialog for each install. Rejected: it is a large UI change that the issue does not need.

## D2. The page has no file install

- Finding: The web UI calls only `/api/gamepacks/catalog/*`. No code calls `/api/gamepacks/install`.
- Decision: Do not add a file install. The page has no file install, so FR-007 covers only the catalog install and update.
- Rationale: FR-011 forbids new install options, and a new file upload is out of scope for issue 336.
- Action: Record the gap in the PR text and in `docs/gamepacks.md`.
- Alternatives: Build a file install now. Rejected: new scope.

## D3. Dry run before install (FR-003, FR-008)

- Finding: Today the dry run is optional. The operator turns on `Dry run` and presses the button.
  With `Dry run` off, the button installs at once.
- Decision: Keep the optional dry run. Add this rule: when take-over is on, a real install needs a valid
  dry run of the same version with take-over on. Else the button is disabled and a hint says to run the dry run first.
  When take-over is off, nothing changes.
- Rationale: Take-over replaces user objects. The operator must see the count first (SC-004). The rule is the same as FR-008.
- Alternatives: Force a dry run for all installs. Rejected: it changes the current behaviour (FR-009 spirit).
  Allow take-over with no dry run. Rejected: it breaks SC-004.

## D4. Stale result

- Decision: Keep `lastDryRun = { key, takeOver, valid, takenOver }` in page state. A change of the check box clears
  `lastDryRun` and the dry run notice. A dry run of another version does not match. The install check compares key and takeOver value.
  A change of the Dry run box keeps `lastDryRun`. This lets the operator run the dry run, turn Dry run off, and install.
- Rationale: Pure state compare. The page starts no hidden request (clarification 1 and loop 1 answer 3).

## D5. Count text

- Decision: Show `Take over N.` in the dry run notice only when the check box was on for that dry run. Use the field
  `takenOver` of the response. It is always present on the back end and is 0 when nothing is taken over.
- Rationale: Matches clarification 2 and FR-004.

## D7. Test runner

- Decision: Jest (`npm test` runs `jest`). The service test file `src/services/__tests__/gamepackCatalog.test.ts` is absent; the setup task creates it.

## D6. Phone

- Finding: `phone.css` already sets `min-height: 44px` and a 24 px box for `.gamepacks-check`. The new label reuses the class.
- Decision: No new phone CSS, unless the long text overflows. Check at 375 px width.
