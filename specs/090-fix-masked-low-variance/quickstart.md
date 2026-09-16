# Quickstart: reproduce and verify 090-fix-masked-low-variance

**Feature**: 090-fix-masked-low-variance | **Issue**: #196 (B-013)

## Reproduce the defect (before the fix)

No live device, capture, or emulator is needed — a solid-colour frame is enough, because the defect
triggers on *any* region that is flat under the mask.

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~MaskedTemplateMatchTests" --nologo
```

Against the pre-fix build, `FlatSceneRegionReportsNoMatch` fails with a confidence of `∞`, and
`ScoreNeverLeavesTheMeasureRange` fails on the same positions.

The shape of the reproduction, if you want it by hand:

1. Build a masked template — `MaskFixtures.CreateMaskedTemplate()` (42x52, circular, 1304 retained
   pixels of 2184).
2. Build a frame that is a single solid grey — `new Mat(new Size(400, 300), MatType.CV_8UC3, new Scalar(200, 200, 200))`.
3. `new TemplateMatcher().MatchAllAsync(frame, tpl, new TemplateMatcherConfig(0.95, 3, 0.3))`.

Pre-fix this returns three matches with `Confidence = ∞`. `Normalization.ClampConfidence(∞)` is
`1.0`, which is exactly what the API reports and exactly what the issue observed.

## Verify the fix

```powershell
dotnet test "C:\src\GameBot\GameBot.sln" --nologo
```

All must be green, with no expected-score assertion loosened (SC-007).

Targeted runs:

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Vision" --nologo
```

```powershell
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~MaskedDetection" --nologo
```

### What to expect

| Case | Expected |
|---|---|
| Masked template, perfectly flat frame | no match; `NoInformationPositionCount > 0` |
| Masked template, one differing pixel (σ ≈ 0.03) | no match |
| Masked template, σ ≈ 2.4 frame | scored normally, well below any live threshold |
| Masked template, genuine badge present | ≥ 0.93, unchanged from feature 089 |
| Masked template, detailed frame, no badge | 0.50–0.60, unchanged |
| Unmasked template, any frame | bit-identical to the pre-fix build |
| Any case at all | score within [−1, 1]; never `∞`, never `NaN` |

## Verify the performance budget

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~MaskedTemplateMatcherBench" --nologo
```

Budget (SC-005): a 42x52 template over a 1080x1920 frame under 300ms, and no more than 2.5x the
unmasked path. Baseline before this change: 189ms masked, 99ms unmasked (1.91x). The hard ceiling is
the 500ms detection timeout — past it a detection is reported as an absence, so a regression here
reintroduces false absences rather than merely being slow.

## Verify the diagnostics

Run a detection whose screen is flat under the mask and look for event `11006`:

```
Detect no-information id=<imageId> suppressedPositions=<n> retainedPixelCount=<n>
```

It is emitted only when at least one position was suppressed, so its presence is the signal. The
response body is unchanged — `masked` and `retainedPixelCount` remain the only mask-related fields
on the wire.

## Checking against the live report

The three captures in the issue live in the PNS automation repository and are not available here, so
the tests are calibrated against the reported statistics instead (spec A-002): a 42x52 reference
image with ~900 of 2184 pixels retained, over a region with a shade standard deviation of 2.43.

If you do have the captures, the check is:

```
POST /api/images/detect {"referenceImageId":"pns-collect-steel","threshold":0.05,"maxResults":1,"captureId":"..."}
```

Pre-fix: `score 1.0000`. Post-fix: a score in the region of `0.52`, or no match — never `1.0000`,
and never the same value for two different reference images at the same position.
