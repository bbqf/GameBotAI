# Contract: OCR duration text for `ocrOffset`

This contract applies to the text that `reschedule-self` `ocrOffset` reads from the screen. The JSON payload shape does not change.

## Accepted forms

1. `<N>d HH:MM:SS` gives N days plus HH:MM:SS.
2. `<N>d MM:SS` gives N days plus MM minutes and SS seconds.
3. `<N>d` gives N days. This applies only when the text has no time token.
4. `HH:MM:SS` and `MM:SS` give the same result as before.

## Rules

- The parser uses the first matching time token. It adds a day token only if the day token is directly before the time token, with only white space between them.
- A day token after the time token, or apart from it, is ignored. Examples: "23:29:10 1d" and "1d left 23:29:10" give 23:29:10.
- White space between the day token and the time token is optional. "d" is not case-sensitive.
- "<N>d" inside a word, such as "Reward" or "3days", is not a day part.
- The parser reads no other unit. "1w 23:29:10" gives 23:29:10.
- A day count with too many digits, or a total that is too large, gives a parse failure. The resolver then uses the fallback with reason `parse-failed`.
- `min` and `max` apply to the total offset, which includes the days.
- A total outside `min` and `max` uses the fallback with reason `out-of-bounds`. The resolver does not clamp.
- The default `max` is 24:00:00. For a read of one day or more, the author sets `max` (for example "2.00:00:00").

## Log line

The line "offset source ocr (read '<text>' -> <offset>)" keeps its layout. The offset uses the standard TimeSpan text, for example `1.23:29:10`.

## API documentation

The `RescheduleSelf` description in `PrimitiveActionSchemaFilter.cs` states the day part and the `max` note.
