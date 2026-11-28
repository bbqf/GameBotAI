# Quickstart: check the navigation (feature 205)

All commands run in `src/web-ui`.

1. `npm run lint` and `npm run build`: no errors.
2. `npx jest`: all tests pass. To run only the navigation tests:
   `npx jest src/types src/components/ui/__tests__/SubTabs.test.tsx src/components/__tests__/AppBar.test.tsx src/components/phone src/__tests__`
   (a subset run can end with exit code 1 only because of the global coverage limits; read the test result lines).
3. Browser tests: `npm run build`, then start `npm run preview -- --host --port 4174 --strictPort` in the background, then
   `npx playwright test tests/e2e/navigation-layout.spec.ts tests/e2e/navigation-layout.mobile.spec.ts --trace off`.
4. Look at the result by hand (1280 px and 360 x 640 px): open the web UI, click each top tab, open Settings and each
   sub-tab, open Authoring; on the phone tap each tab, open More, open Configuration, tap Back.
