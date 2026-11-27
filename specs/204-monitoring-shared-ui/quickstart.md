# Quickstart: check the Monitoring area

```powershell
npm --prefix "C:\src\GameBot\src\web-ui" test
npm --prefix "C:\src\GameBot\src\web-ui" run lint
npm --prefix "C:\src\GameBot\src\web-ui" run build
npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" monitoring-layout --trace off
```

Note: if a local browser test times out at a page load or shows an empty page, start the preview server yourself
(`npm run build`, then `npm run preview -- --host --port 4174 --strictPort`) and run the tests again. CI is the final
check.

## Manual check

1. Open `?area=monitoring` at 1280 px. Compare the list with `docs/ui-guidelines/1-list-pattern.png`.
2. Click a row. Compare the detail with mockup B of `docs/ui-guidelines/2-form-detail-editor-logs.png`.
3. Select **Restart emulator** and **Cancel**. Make sure that no call is sent.
4. Make the window 360 px wide. Make sure that the rows are 64 px high and the footer button shows.
