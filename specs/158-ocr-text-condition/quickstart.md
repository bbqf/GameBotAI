# Quickstart: OCR Text Condition

## Use the condition

1. Find the pixel region of the text. Use `POST /api/ocr/read` with the region to check the text that the engine reads.
2. Save a sequence with an `If` step. Set the condition to `ocrText` with `region` and `expected`:

```json
{
  "type": "ocrText",
  "region": { "x": 120, "y": 640, "width": 400, "height": 48 },
  "expected": "0"
}
```

3. Run the sequence on a session. The `then` branch runs when the region shows `0`.
4. Open the run log. Find the line `ocrText: read='0' expected='0' result=true`.

## Use a looser match

Use `pattern` in place of `expected`. Example: `"pattern": "left:\\s*0$"` matches "Interaction attempts left: 0".

## Read a countdown

Add `"parser": "hh:mm:ss"`. The compare uses the parsed text, for example `02:10:35`. The name match ignores case: the service stores `HH:MM:SS` as `hh:mm:ss`.

## Read a failure reason

When the condition is false because of a failure, the run log line has `reason=<code>`. The codes are `no-session`, `capture-failed`, `ocr-unavailable`, `region-outside-frame`, `region-invalid`, `ocr-error`, and `parse-failed`. A `pattern` time-out is a plain mismatch: the log says `pattern timed out` and has no code. An empty read is compared like any text. Step-through does not read the screen. It shows the note `ocrText is not evaluated`.

## Check the feature

1. Build: `dotnet build C:\src\GameBot\GameBot.sln`
2. Run the tests: `dotnet test C:\src\GameBot\GameBot.sln`
3. Open `/swagger` and find the schema `OcrTextConditionContract`.
4. Save a sequence with both `expected` and `pattern`. The service gives a 400 that names both fields.
