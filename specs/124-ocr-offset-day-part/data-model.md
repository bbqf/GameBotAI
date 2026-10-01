# Data Model: OCR Offset Day Part

No stored data changes. No new type.

## OCR duration text (input)

A string from OCR. It can contain these forms, with other text around them:

| Form | Example | Result |
|------|---------|--------|
| `<N>d HH:MM:SS` | `1d 23:29:10` | 1 day 23:29:10 |
| `<N>d MM:SS` | `1d 05:30` | 1 day 5 minutes 30 seconds |
| `<N>d` (no time token) | `2d` | 2 days |
| `HH:MM:SS` | `23:29:10` | 23:29:10 (unchanged) |
| `MM:SS` | `05:30` | 5 minutes 30 seconds (unchanged) |

Rules:

- The parser finds the first time token first. This rule is not changed.
- A day token counts only when it is directly before the first time token. Only white space is between them.
- A day token after the time token, or apart from it, is ignored. "23:29:10 1d" gives 23:29:10. "1d left 23:29:10" gives 23:29:10.
- "<N>d" alone counts only when the text has no time token.
- White space between the day token and the time token is optional. The letter "d" is not case-sensitive.
- A day token counts only when no letter or digit comes before its digits, and no letter comes after the "d".
- The "d" follows the digits directly. "1 d 23:29:10" is not required to read as a day part.
- A digit run is read as a whole. "11d" is 11 days.
- Other units are not read. "1w 23:29:10" gives 23:29:10.
- A day count that does not fit in an integer, or a total that is too large, gives a parse failure.

## Parsed offset (output)

A `TimeSpan` that includes the days. `CooldownDurationParser.TryParse(string?, out TimeSpan)` keeps its signature.

`SelfRescheduleOcrOffset.Min` and `.Max` compare with the total. A total outside the limits uses the fallback with reason "out-of-bounds". The resolver does not clamp. The default `Max` stays 24:00:00.

## State transitions

None.
