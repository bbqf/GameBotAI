# Research: OCR Offset Day Part

All items from the Technical Context are resolved. No "NEEDS CLARIFICATION" remains.

## Decision 1: Day token pattern

- **Decision**: Use `(?<![A-Za-z0-9])(\d+)[dD](?![A-Za-z])` with the culture-invariant option and the 200 ms timeout. The "d" follows the digits directly.
- **Rationale**: This matches FR-004 and FR-005. The case of "d" is free. A word such as "Reward" or "3days" gives no match. The spec does not require a match for "1 d" (edge case list).
- **Alternatives considered**: Allow white space between the digits and "d" (`\d+\s*d`). Rejected: the spec does not require it, and it adds false matches.

## Decision 2: How the day token joins the time token

- **Decision**: Find the first time token with the existing regex, as now. If a time token exists, take the text before it. Match the day pattern at the end of that text, with only white space after it (`...\s*\z`). If the match succeeds, add the days. If no time token exists, use the first day token in the whole text.
- **Rationale**: This is FR-003, FR-006 and FR-007. "23:29:10 1d" and "1d left 23:29:10" keep the old result 23:29:10. The end anchor also enforces FR-005 on the letter after "d": a day token such as "3days" cannot end the text before the time token.
- **Alternatives considered**: (a) Add any day token in the text. Rejected: it changes results that work today. (b) One combined regex for day and time. Rejected: it makes it harder to keep the old time rule unchanged.

## Decision 3: Text that the day pattern reads

- **Decision**: Match the day pattern on the text after `NormalizeDigits`, the same text as the time token. The normalization keeps the length, so the positions agree.
- **Rationale**: OCR can read "1d" as "ld" or "Id". The existing normalization turns "l", "I" and "|" into "1". One text for both patterns keeps one rule.
- **Risk**: A standalone word "Id" (or "ld") with no time token reads as "1d", which is 1 day. The result is one day, and the `min` and `max` limits and the fallback still apply. A word such as "Hold" is safe, because a letter comes before the "1". The tests record this case. The risk is accepted.
- **Alternatives considered**: Match the day pattern on the raw text. Rejected: it misses "ld 23:29:10", which is a likely OCR error.

## Decision 4: Overflow

- **Decision**: Parse the day count with the existing component parser. There are two overflow cases. Case 1: a day count that does not fit in `long` gives failure (spec edge case, FR-009). Case 2: a day count that fits but gives a total outside the `TimeSpan` range (for example "2147483647d 00:00:01") also gives failure. The resolver then gives the reason "parse-failed", not "ocr-error". Compute `days * 86400 + hours * 3600 + minutes * 60 + seconds` in a `checked` block. Catch `OverflowException` and also `ArgumentOutOfRangeException`.
- **Rationale**: On .NET 9, `TimeSpan.FromSeconds(long)` throws `ArgumentOutOfRangeException` when the value is outside the `TimeSpan` range. The existing code catches only `OverflowException`. A large day count reaches that range easily. The new catch keeps the parser result "false" and not an exception. The resolver would otherwise report "ocr-error" and not "parse-failed". The result of the old forms does not change.
- **Alternatives considered**: Compute with `TimeSpan` operators. Rejected: the seconds path is simple and the existing tests cover it.

## Decision 5: Min and max limits, and the log line

- **Decision**: No code change. The resolver compares the parsed `TimeSpan` with `Min` and `Max`. A value outside the limits uses the fallback with reason "out-of-bounds". The log line prints the standard `TimeSpan` text, for example `1.23:29:10`.
- **Rationale**: Both already work on the total offset (FR-010, FR-011, FR-013). The payload validator accepts a `max` with days, because it uses `TimeSpan.TryParse`.
- **Alternatives considered**: Raise the default `max` above 24 hours. Rejected: the spec keeps the default (Clarification F2, User Story 3 scenario 4). The author sets `max`.

## Decision 6: Documentation

- **Decision**: Change the `RescheduleSelf` text in `PrimitiveActionSchemaFilter.cs`. State that the OCR text can have a day part "<N>d" before the time, and that `max` must allow the total. Update the XML comment of `CooldownDurationParser`. Update `docs/architecture.md` only if it states the OCR text format. At the time of this plan, line 239 mentions `ocrOffset` only for `Cancel`, so no change is expected.
- **Rationale**: FR-012 and Constitution Principle V.
- **Alternatives considered**: None.

## Decision 7: Test approach

- **Decision**: Add theory cases to `CooldownDurationParserTests` for each accepted and rejected form. Add resolver tests for `max` "2.00:00:00", `max` "1.00:00:00", `min` "1.00:00:00", the default `max`, and the log text with the day part. Write the failing tests before the parser change.
- **Rationale**: Constitution Principle II and FR-015. Each test case is deterministic and fast.
- **Alternatives considered**: A contract test through the HTTP API. Rejected: the payload shape does not change.
