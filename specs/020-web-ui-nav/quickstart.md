# Quickstart: Implementing Web UI Navigation Restructure

## Prerequisites
- Node 20+, npm
- From repo root: `cd src/web-ui && npm install`

## Steps
1. **Create top-level tabs**: Add a navigation shell with Authoring, Execution, Configuration tabs; active-state styling; collapse to a simple menu below ~768px while keeping labels readable.
2. **Wire routing/state**: Ensure each tab routes to its area; preserve one-click switching; maintain focus/aria-current on active item.
3. **Authoring grouping**: Move Actions, Sequences, Commands under the Authoring area; remove Triggers entry and delete legacy Trigger routes (standard not-found handles any direct hits).
4. **Configuration header**: Place host/token controls at the top of the Configuration area; ensure layout separation from authoring content.
5. **Execution placeholder**: Add an empty-state view describing upcoming execution features with optional links back to Authoring/Configuration.
6. **Responsive behavior**: Implement collapse behavior and verify keyboard navigation works in both expanded and collapsed modes.
7. **Tests**: Update/add Jest + React Testing Library coverage for navigation state, redirects, and collapsed menu; add/adjust Playwright e2e smoke for tab switching and legacy Trigger redirect.
8. **Quality gates**: Run `npm run lint`, `npm test`, and `npm run e2e` (as applicable) from `src/web-ui`; ensure performance budget (p95 tab switch <150ms after assets loaded) is documented if measured.

## Current state (2025-12-30)

- Navigation: Tabs (Authoring, Configuration, Execution) collapse to menu near 768px; active tab uses `aria-current`; keyboard access works in both expanded/collapsed modes.
- Routes: Triggers removed; legacy `/triggers/*` hits the standard not-found view.
- Configuration: Host/token inputs live at the top of Configuration with no authoring content bleed.
- Quality gates (src/web-ui):
	- `npm run lint` ✅
	- `npm test` ✅
	- `npm run e2e -- --project=chromium` ✅
- Perf note: Tab switch budget is p95 <150ms. Run the probe below against a running `npm run dev -- --host --port 4173` session to record current numbers:

```powershell
cd C:/src/GameBot/src/web-ui
node -e "const { chromium } = require('playwright'); (async()=>{const browser=await chromium.launch({headless:true});const page=await browser.newPage({ viewport:{width:1280,height:720}});const tStart=Date.now();await page.goto('http://localhost:4173/',{waitUntil:'networkidle'});const tNav=Date.now()-tStart;const t0=Date.now();await page.getByRole('tab',{name:'Configuration'}).click();await page.getByRole('heading',{name:'Configuration'}).waitFor({state:'visible'});const tSwitch=Date.now()-t0;console.log(JSON.stringify({coldLoadMs:tNav,tabSwitchMs:tSwitch}));await browser.close();})();"
```
- Discoverability (SC-002): Host/token fields are first-content in Configuration with heading and labels; one-click from landing via Configuration tab.
