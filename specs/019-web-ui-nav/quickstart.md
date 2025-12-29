# Quickstart: Implementing Web UI Navigation Restructure

## Prerequisites
- Node 20+, npm
- From repo root: `cd src/web-ui && npm install`

## Steps
1. **Create top-level tabs**: Add a navigation shell with Authoring, Execution, Configuration tabs; active-state styling; collapse to a simple menu below ~768px while keeping labels readable.
2. **Wire routing/state**: Ensure each tab routes to its area; preserve one-click switching; maintain focus/aria-current on active item.
3. **Authoring grouping**: Move Actions, Sequences, Commands under the Authoring area; remove Triggers entry and redirect legacy Trigger routes to Authoring.
4. **Configuration header**: Place host/token controls at the top of the Configuration area; ensure layout separation from authoring content.
5. **Execution placeholder**: Add an empty-state view describing upcoming execution features with optional links back to Authoring/Configuration.
6. **Responsive behavior**: Implement collapse behavior and verify keyboard navigation works in both expanded and collapsed modes.
7. **Tests**: Update/add Jest + React Testing Library coverage for navigation state, redirects, and collapsed menu; add/adjust Playwright e2e smoke for tab switching and legacy Trigger redirect.
8. **Quality gates**: Run `npm run lint`, `npm test`, and `npm run e2e` (as applicable) from `src/web-ui`; ensure performance budget (p95 tab switch <150ms after assets loaded) is documented if measured.
