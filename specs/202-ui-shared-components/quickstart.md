# Quickstart: check the shared components and the Queues list

## Unit tests and lint

```powershell
npm --prefix "C:\src\GameBot\src\web-ui" test
npm --prefix "C:\src\GameBot\src\web-ui" run lint
npm --prefix "C:\src\GameBot\src\web-ui" run build
```

## Browser tests

```powershell
npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" queues-list-layout queues-actions-width --trace off
```

The desktop file runs in the `chromium` project. The phone file (`queues-list-layout.mobile.spec.ts`) runs in the
`mobile` project.

## Manual check

1. Start the dev server: `npm --prefix "C:\src\GameBot\src\web-ui" run dev`.
2. Open `http://localhost:5173/?area=queues` at a window of 1280 px. Compare with `docs/ui-guidelines/1-list-pattern.png`.
3. Start a queue. Make sure that no column moves.
4. Open "⋮" of a stopped queue and select Delete. Make sure that the dialog shows "Delete queue <name>?".
5. Make the window 360 px wide. Make sure that the rows are 64 px high and that no sideways scroll occurs.
