# Quickstart: Report which image of a list matched

1. Make a sequence. Step `wait-quest` is a `WaitForImage` step with the image list `quest-a`, `quest-b`.
2. Add a later step with the condition `{ "type": "matchedImage", "stepRef": "wait-quest", "imageId": "quest-b" }`.
3. Add a log or failure text that uses `{{step.wait-quest.matchedImageId}}`.
4. Run the sequence while `quest-b` is on screen.
5. Check: the condition is true. The text holds `quest-b`. The per-step run log of `wait-quest` shows `matchedImageId: quest-b`.
6. Run again while `quest-a` is on screen. The condition is false and the text holds `quest-a`.

Tests: `dotnet test` for the Domain and Service projects, `npm test` in `src/web-ui`.
