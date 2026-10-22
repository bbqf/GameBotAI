# Quickstart: Gamepack Install Failure Explanation

## Check the API

1. Make a gamepack file with 20 sequences. Create 20 user sequences with the same names.
2. Send a dry run without take-over:
   `POST /api/gamepacks/install?dryRun=true` with the file as the body.
3. Check the answer:
   - `valid` is false.
   - `summary` comes before `problems`. It has one entry: `conflict`, `count` 20, the take-over remedy, and `resolvedByTakeOver` true.
   - `wouldInstallWithTakeOver` is true.
   - Each problem has `takeOverResolves` true and a `remedy`.
4. Add one sequence with a missing command reference to the file. Send the dry run again. The summary has two entries
   (reference, then conflict) and `wouldInstallWithTakeOver` is false.
5. Send the install without `dryRun`. The answer is 422 and has the same summary. The instance does not change.
6. Send the dry run with `takeOver=true` for a valid file. `valid` is true, the summary has no entries, and
   `wouldInstallWithTakeOver` is true.

## Check the page

1. Start the service and the web UI. Open the Gamepacks page.
2. Set a catalog that lists a gamepack with user-owned conflicts. Keep the take-over box off.
3. Turn on Dry run. Press Install. The notice shows the count and the remedy for each category, and tells if take-over would resolve all problems.
4. Turn off Dry run. Press Update or Install. The notice shows the same summary.
5. Stop the service and press Install. The notice shows a cause. It is never empty.
6. Resize the browser to 375 px wide. The notice wraps. There is no horizontal scroll.

## Run the tests

- Back end: `dotnet test` for `tests/unit` and `tests/contract` (filter `Gamepack`).
- Web UI: `npm test` in `src/web-ui` (files `GamepacksPage.test.tsx` and `gamepackCatalog.test.ts`).
