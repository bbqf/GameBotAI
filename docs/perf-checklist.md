# Unified Authoring UI Performance Checklist

Targets (per spec):
- Form edits: ≤ 100 ms from input to paint.
- Reorder interactions: ≤ 200 ms to reflect new order.
- Initial load: < 1.5 s on broadband (dev build acceptable).

How to measure (quick pass):
1) Start web UI (`npm run dev`) and open the page under test (Actions, Commands, Triggers, Games, Sequences).
2) Open Chrome DevTools Performance panel; record a 3–5s trace while typing in a required field (Name) and clicking Save.
3) Record input latency from the trace (scripting + rendering for the keystroke) and note if under 100 ms.
4) Add/reorder array items (steps/actions/commands) and measure the reorder action (button click → list update) stays under 200 ms.
5) Reload the page with DevTools network throttling set to “Online” (broadband) and capture load time to first paint and page ready; confirm under 1.5 s.
6) If any threshold is exceeded, capture a new trace with “Screenshots” enabled and note the longest task, then file a follow-up.

Reporting
- Record findings (page, metric values, date) and any bottlenecks found.
- File issues for regressions with attached trace or screenshots.
