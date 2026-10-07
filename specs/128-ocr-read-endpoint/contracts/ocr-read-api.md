# Contract: `POST /api/ocr/read`

Tag: Emulators. Operation name: `ReadOcrRegion`. The endpoint sends no input to the emulator.

## Request

```json
{ "serial": "emulator-5558", "region": { "x": 120, "y": 640, "width": 300, "height": 60 }, "parser": "hh:mm:ss" }
```

```json
{ "captureId": "3f2a...", "region": { "x": 120, "y": 640, "width": 300, "height": 60 } }
```

Set exactly one of `serial` and `captureId`. `region` is required. `parser` is optional.

## Response 200

```json
{
  "text": "Refresh time: 02:10:35",
  "confidence": 0.91,
  "source": "serial",
  "frameWidth": 540,
  "frameHeight": 960,
  "parser": "hh:mm:ss",
  "parsed": { "value": "02:10:35", "totalSeconds": 7835 },
  "parseFailureReason": null
}
```

A parse failure is also 200. `parsed` is null and `parseFailureReason` is `text did not parse as hh:mm:ss`.

## Errors

All errors use `{ "code": "...", "message": "..." }`. See [data-model.md](../data-model.md) for the code list.

| Case | Status | Code |
|------|--------|------|
| No source, two sources, no `region`, no body, bad JSON | 400 | `invalid_request` |
| Unknown parser | 400 | `unknown_parser` |
| Width or height not above zero | 400 | `invalid_region` |
| Serial has no running session | 404 | `serial_not_found` |
| Capture id unknown or expired | 404 | `capture_not_found` |
| Frame not available, null crop, or stored PNG does not decode | 502 | `capture_failed` |
| Host has no capture service | 503 | `capture_unavailable` |
| No OCR engine, host is not Windows, or engine throws | 503 | `ocr_unavailable` |
| Region not fully inside the frame | 400 | `invalid_region` (message gives the frame size) |

### Check order

When a request has more than one fault, the first fault in this list wins:

1. No lookup needed: body, source count, `region` present, parser name, region size above zero. Status 400.
2. Lookup of the serial or the capture id. Status 404.
3. Capture or decode. Status 502 (or 503 `capture_unavailable`).
4. Engine availability. Status 503.
5. Region inside the frame. Status 400.

For a serial, the endpoint uses the first running session that matches. The parser name `hh:mm:ss` is compared
without case. A parse failure is not an error (status 200).

## OpenAPI

The path `/api/ocr/read` shows with the request and response schemas. `OcrReadSchemaFilter` sets a description on
each property. The operation has a summary and a description that state "no input is sent to the emulator".

## Guarantees

- Raw text equals the raw text that `ocrOffset` reads for the same frame and region (shared `OcrRegionReader`).
- No cache. Each call runs the capture or the stored read, and the engine, again.
- No input to the emulator.
