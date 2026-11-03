# Quickstart: Use default label

1. Add `defaultDisplayText` to `src/web-ui/src/components/parameters/types.ts`. Edit `ParameterBindingForm.tsx`: label, `aria-label`, default text, code comments. Edit the comments in `ParameterFormPreview.tsx`. Add the rule `.parameter-binding-default` to `src/web-ui/src/styles.css`.
2. Update the queries in `ParameterBindingForm.test.tsx` and `QueueSchedulingAreas.parameters.test.tsx` from `Inherit <name>` to `Use default <name>`.
3. Add tests for these default types: choice, number, image, empty string, null, undefined. Add a test that the box is visible when there is no default.
4. Add the CHANGELOG entry (STE). Add the row for 176 in `specs/STATUS.md` (same format as row 175).
5. Run the web-ui tests: `npm --prefix "C:\src\GameBot\src\web-ui" test`. Run the backend tests with `dotnet test` and an absolute solution path, to confirm no change in resolve tests.
6. Search `docs/` for "inherit". Fix any match that names this box.
7. Manual check at the narrowest phone width: a long default wraps, no clipped text, 44 px touch target. Put the result in the PR description with the SC-004 reviewer result.
8. At push time, bump `installer/versioning/version.override.json` (patch on `origin/master` plus 1).
