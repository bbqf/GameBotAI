# Quickstart: 058 Tap-Point Jitter

## What Changed

Every tap and swipe sent to the device now lands within a small random offset of its target coordinates instead of always landing on the exact same pixel. This happens automatically for all taps and swipes — authored directly, resolved via image detection, replayed from a recording, or submitted via the raw `POST /sessions/{id}/inputs` API — with no per-step setup or opt-out. If you need pixel-exact input (e.g., calibration via the raw API), set the radius to 0.

## Configuration

One setting controls jitter. It has a sensible default and works out of the box.

| Setting | Env Variable | Default | Description |
|---------|-------------|---------|-------------|
| Jitter Radius | `GAMEBOT_TAP_JITTER_RADIUS_PX` | 5 | Maximum per-axis random offset (pixels). `0` disables jitter. |

### Example: Default behaviour (no config needed)

With the default radius of 5px, a tap targeting `(100, 200)`:
1. Computes an independent random X offset in `[-5, +5]` and an independent random Y offset in `[-5, +5]`.
2. Sends the resulting coordinates (e.g. `(103, 197)`) to the device, clamped so neither value goes below 0.
3. Repeats this independently every time the step runs — consecutive runs land at slightly different points.

For a swipe from `(50, 50)` to `(500, 800)`, the start and end points are jittered **independently** — the effective swipe length/direction may vary slightly between runs.

### Example: Disabling jitter

Set `GAMEBOT_TAP_JITTER_RADIUS_PX=0`. All taps and swipes land exactly on their configured/resolved target, with no randomness — useful for precise testing or troubleshooting.

### Example: Larger jitter

Set `GAMEBOT_TAP_JITTER_RADIUS_PX=20` to allow up to ±20px of variation per axis — useful for UIs with larger touch targets where more variation looks more natural.

### Invalid values

A negative value (or anything that fails to parse as a non-negative integer) is ignored and the system falls back to the default of 5.

## Monitoring

- **Execution logs**: Each tap/swipe step's log entry shows both the originally targeted coordinates and the actual (post-jitter) coordinates sent to the device, e.g. `"Tap targeted (100,200), executed at (103,197)."`.
- **Step outcomes / API**: `PrimitiveTapStepOutcome` (and its API DTO equivalents) include new `ExecutedPoint` (for taps) and `TargetSwipe`/`ExecutedSwipe` (for swipes) fields alongside the existing `ResolvedPoint`.

## UI Configuration

The jitter radius appears in the general Configuration variables list (same view as ADB retry settings), showing its current effective value, default, and source (default/file/environment) — consistent with all other `GAMEBOT_*` parameters. There is **no** dedicated jitter control in the command authoring UI or the execution UI; the configuration list is the only place to view or change it.

## No Impact On

- Sequence/command authoring — no new fields, no new step options.
- Existing step outcome consumers that only read `ResolvedPoint` — it continues to represent the pre-jitter target, unchanged.
- Existing API endpoints — no new endpoints; jitter config is visible via the existing `GET /api/config`.
- Tests using `RecordingSessionManager` fakes — jitter is applied only inside the real `SessionManager`, so fake-based unit tests that assert exact dispatched coordinates are unaffected.
