# Quickstart: check the Notifications area

```powershell
npm --prefix "C:\src\GameBot\src\web-ui" test
npm --prefix "C:\src\GameBot\src\web-ui" run lint
npm --prefix "C:\src\GameBot\src\web-ui" run build
npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" notifications-layout --trace off
```

Note: if a phone test shows an empty page, the preview server did not start in time. Run `npm run build`, start
`npm run preview -- --host --port 4174 --strictPort` in a second window, and run the tests again.

## Manual check

1. Open `?area=notifications` at 1280 px. Compare with `docs/ui-guidelines/1-list-pattern.png` (list) and
   `docs/ui-guidelines/2-form-detail-editor-logs.png` (form A).
2. Select **Add target**. Make sure that the side panel is 480 px wide and the list stays visible.
3. Select **Save** with an empty Name. Make sure that "Enter a name." shows and no call is sent.
4. Make the window 360 px wide. Open a target. Make sure that the form is a full page with a back arrow.
