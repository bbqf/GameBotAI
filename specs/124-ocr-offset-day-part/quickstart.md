# Quickstart: OCR Offset Day Part

## Verify the parser

1. Run the parser tests:
   `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~CooldownDurationParserTests"`
   (use the real unit test project path in the repository).
2. Check these results:
   - "1d 23:29:10" gives 1.23:29:10.
   - "Free in 1d 23:29:10" gives 1.23:29:10.
   - "1d23:29:10" and "1D 23:29:10" give 1.23:29:10.
   - "1d 05:30" gives 1.00:05:30.
   - "2d" and "Free in 2d" give 2.00:00:00.
   - "23:29:10" gives 23:29:10 and "05:30" gives 00:05:30.
   - "23:29:10 1d", "1d left 23:29:10", "1w 23:29:10" and "Reward 23:29:10" give 23:29:10.
   - "Reward" and "3days" give a parse failure.
   - "Id" and "ld" with no time token give 1.00:00:00 (the digit fix runs first; accepted OCR risk).
   - A day count with too many digits ("999999999999d 00:00:01") gives a parse failure.
   - A day count in range of an integer but with a total outside the TimeSpan range ("2147483647d 00:00:01") gives a parse failure and no exception.

## Verify the resolver

1. Run `OcrOffsetResolverTests`.
2. With `max` "2.00:00:00" and text "1d 23:29:10", the resolver returns source Ocr and 1.23:29:10.
3. With `max` "1.00:00:00" or with the default `max` (24:00:00), the same text gives the fallback with reason `out-of-bounds`.
4. With `min` "1.00:00:00" and text "23:29:10", the result is the fallback with reason `out-of-bounds`.
5. The log line "offset source ocr" shows `1.23:29:10`.

## Author note

To book a timer of one day or more, set `ocrOffset.max` to a value above 24:00:00, for example "2.00:00:00".
