# Quickstart: Image Match Detections

## Upload or verify a reference image

```bash
curl -X POST http://localhost:5187/images \
  -H "Content-Type: application/json" \
  -d '{"id":"Home","data":"<base64_png>","overwrite":true}'
```

## Detect matches for a reference image

```bash
curl -X POST http://localhost:5187/images/detect \
  -H "Content-Type: application/json" \
  -d '{
    "referenceImageId":"Home",
    "threshold":0.85,
    "maxResults":10,
    "overlap":0.3
  }'
```

Example response:

```json
{
  "matches": [
    { "bbox": {"x":0.12,"y":0.45,"width":0.08,"height":0.08}, "confidence":0.93 },
    { "bbox": {"x":0.61,"y":0.47,"width":0.08,"height":0.08}, "confidence":0.89 }
  ],
  "limitsHit": false
}
```

## Notes

- Coordinates are normalized to the current screenshot size.
- Confidence is clamped to [0,1]; internally the detector uses NCC (`TM_CCOEFF_NORMED`) and filters by `threshold` (default 0.8) before clamping.
- Set `maxResults` and `overlap` for stricter de-duplication.
