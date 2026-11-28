# Quickstart: check Sequences (feature 207)

All commands run in `src/web-ui`.

1. `npm run lint` and `npm run build`: no errors.
2. `npx jest`: all tests pass. A subset run (for example `npx jest src/pages/__tests__/SequencesPage src/components/authoring`) can end with exit code 1 only because of the global coverage limits; read the test result lines.
3. Browser tests: `npm run build`, start `npm run preview -- --host --port 4174 --strictPort` in the background, then `npx playwright test tests/e2e/authoring-sequences-layout.spec.ts --trace off`, then all files.
4. By hand at 1280 px: open Authoring > Sequences, filter, open a row, change a field, use the back arrow (prompt), save, delete from the row menu, open "Step through" on a saved sequence.
