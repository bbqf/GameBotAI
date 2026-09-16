# Quickstart: Reference Image Transparency Masks

**Feature**: 089-reference-image-alpha-mask

## For operators: masking a reference image

1. Open the crop in any image editor that supports transparency.
2. Erase everything that is not the target — the corners around a circular pin, the
   background behind an irregular icon. Leave the target itself fully opaque.
3. Save as PNG **with transparency** (do not flatten onto a background colour).
4. Upload it as usual:

```bash
curl -X POST http://localhost:8080/api/images -F "id=pns-collect-steel" -F "file=@steel-masked.png"
```

5. Detect with the same threshold you used before — the score scale has not changed:

```bash
curl -X POST http://localhost:8080/api/images/detect -H "Content-Type: application/json" -d '{"referenceImageId":"pns-collect-steel","threshold":0.05,"maxResults":1,"captureId":"<capture>"}'
```

The response now reports whether the mask was used:

```json
{ "matches": [ { "score": 0.9312, "...": "..." } ], "limitsHit": false, "masked": true, "retainedPixelCount": 1421 }
```

If `masked` is `false` on an image you masked, the transparency did not survive your export —
re-save as PNG with an alpha channel.

### What changes for existing images

Nothing. An image with no transparency scores exactly what it scored before, so every
threshold in every live sequence stays calibrated. No sequence needs editing: a masked image
is used by a wait-for-image or tap-on-image step the moment it replaces the unmasked one.

### What gets refused

An image whose mask leaves fewer than 16 opaque pixels is rejected at upload with a 400 and
a message naming the retained count. A mask that small matches almost anything.

## For developers: the moving parts

| Concern | Where |
|---|---|
| Alpha-preserving decode | `src/GameBot.Domain/Vision/TemplateImageDecoder.cs` — use it for every **template** decode; screenshots keep `ImreadModes.Color` |
| Mask derivation | `src/GameBot.Domain/Vision/TemplateMask.cs` — alpha >= 128 retained; returns false for an all-opaque alpha |
| Masked scoring | `src/GameBot.Domain/Vision/MaskedTemplateMatch.cs` — masked ZNCC via three `TM_CCORR` correlations |
| Branch point | `src/GameBot.Domain/Vision/TemplateMatcher.cs` — masked path only when a mask exists; the unmasked `CCoeffNormed` call is untouched |
| Upload rejection | `src/GameBot.Service/Endpoints/ImageDetectionsValidation.cs` |

### The one rule that matters

**Never route an unmasked template through the masked formulation.** They are algebraically
equal but not bit-identical, and every detection threshold across live queues is calibrated
against today's exact scores. The branch in `TemplateMatcher` is what makes zero score drift
structural rather than approximate.

### Adding a new template-consuming path

Decode with `TemplateImageDecoder.Decode(bytes)` and pass the Mat to `ITemplateMatcher`. The
mask travels inside the Mat, so the path honours it without knowing masks exist.

## Running the tests

```powershell
dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~TemplateMask|FullyQualifiedName~TemplateMatcher|FullyQualifiedName~MaskedDetection"
```

Key cases:

- **Score identity** — an opaque template scores bit-identically through the new code.
- **Masked badge** — the same badge over two different backgrounds passes the 0.85 gate
  masked and fails it unmasked; the control frame without the badge stays below the gate.
- **Background independence** — repainting the pixels behind the transparent region does not
  move the score.
- **Degenerate masks** — fully transparent and under-16-pixel masks are refused at upload;
  a uniform retained region reports no match.
- **Performance** — masked detection stays inside the 500 ms detection timeout.
