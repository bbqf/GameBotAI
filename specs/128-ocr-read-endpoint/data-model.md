# Data Model: OCR Read Endpoint

The endpoint keeps no state. All types are request and response shapes.

## OcrReadRequest

| Field | Type | Rule |
|-------|------|------|
| `serial` | string, optional | Set exactly one of `serial` and `captureId`. A blank string counts as not set. |
| `captureId` | string, optional | See `serial`. Must name a capture in `CaptureSessionStore`. |
| `region` | OcrRegionDto, required | A missing `region` gives 400 `invalid_request`. |
| `parser` | string, optional | When set, must be a name in the parser registry (`hh:mm:ss`). Compared without case. |

## OcrRegionDto

| Field | Type | Rule |
|-------|------|------|
| `x` | int | `>= 0` |
| `y` | int | `>= 0` |
| `width` | int | `> 0` (checked before the lookup) |
| `height` | int | `> 0` (checked before the lookup) |

The region lies in pixels of the frame. It must be fully inside: `x + width <= frameWidth` and
`y + height <= frameHeight`. The sum uses `long` math. The service checks this after the frame is known.
The same step runs for both sources. For a stored capture, the frame size is the size of the decoded picture.
A region that ends at the frame edge is valid. A region one pixel beyond is invalid.

## OcrReadResponse (200)

| Field | Type | Meaning |
|-------|------|---------|
| `text` | string | Raw text from the engine. Empty when the region has no text. |
| `confidence` | number | Confidence value from the engine. Same meaning as in the `ocrOffset` step. |
| `source` | string | `serial` or `captureId`. |
| `frameWidth` | int | Width of the frame in pixels. |
| `frameHeight` | int | Height of the frame in pixels. |
| `parser` | string or null | The parser name that ran. Null when the request has no parser. |
| `parsed` | OcrParsedValue or null | Set when the parser is named and the text parses. |
| `parseFailureReason` | string or null | Set when the parser is named and the text does not parse. |

## OcrParsedValue

| Field | Type | Meaning |
|-------|------|---------|
| `value` | string | The duration as `hh:mm:ss` text. Hours can pass 24. |
| `totalSeconds` | number | The duration in seconds. |

## OcrErrorResponse

`{ "code": string, "message": string }`. Codes, in check order:

| Code | Status | Cause |
|------|--------|-------|
| `invalid_request` | 400 | No body, bad JSON, no source, two sources, or no `region`. |
| `unknown_parser` | 400 | Parser name not in the registry. The message lists the supported names. |
| `invalid_region` | 400 | Size not above zero, or region not fully inside the frame. The message gives the frame size when it is known. |
| `serial_not_found` | 404 | No running session for the serial. |
| `capture_not_found` | 404 | Unknown or expired capture id. |
| `capture_failed` | 502 | The session has no frame, the crop gives a null image, or a stored PNG does not decode. |
| `capture_unavailable` | 503 | The host has no capture service. |
| `ocr_unavailable` | 503 | No OCR engine (also a host that is not Windows), or the engine threw an error. |

## Internal types

| Type | Purpose |
|------|---------|
| `OcrRegionReadResult` | Result of `OcrRegionReader.Read`: text and confidence, or a flag that says the crop failed. |
| `OcrReadOutcome` | Result of `OcrReadService`: a response, or an error code with a status. The endpoint maps it to HTTP. |

## State transitions

None. Each request is independent. The endpoint does not cache a frame or a read.
