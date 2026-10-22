# Quickstart: Gamepacks Page Take-Over Option

## Check by hand

1. Start the service and the web UI. Open the Gamepacks page.
2. Set a catalog that holds a gamepack with objects that have the same names as user objects.
3. Turn on `Dry run`. Press `Update`. Check that the notice shows the conflicts and has no `Take over N.` text.
4. Turn on the take-over check box. Check that the notice is cleared. Check that the take-over box is off after a page reload.
5. Press `Update` with `Dry run` on. Check that the notice shows `Take over N.`
6. Turn off `Dry run`. Check that the notice stays. Press `Update`. Check that the install works and the objects belong to the gamepack.
7. Turn on `Dry run`, then change the take-over check box, then turn off `Dry run`. Check that the button is disabled and the row shows `Run the dry run with take-over first.`
8. Open the page at 375 px width. Check that the check box has a 44 px target and that nothing scrolls sideways.

## Run the tests

Jest is the test runner.

```powershell
& "C:\src\GameBot\src\web-ui\node_modules\.bin\jest.cmd" --rootDir "C:\src\GameBot\src\web-ui" src/pages/__tests__/GamepacksPage.test.tsx src/services/__tests__/gamepackCatalog.test.ts
```
