# Quickstart: Any-of image condition and WaitForImage for a list parameter

1. Declare a parameter `quests` with kind `image`, `multiple: true`, and allowed images `quest_a`, `quest_b`, `quest_c` (feature 179).
2. Save a sequence step with the condition `{ "type": "imageVisible", "imageId": "{{quests}}" }`. The response has the `static_check_skipped` warning.
3. Save a command with a `waitForImage` step whose image key is `{{quests}}`.
4. Put a queue entry value `quests = [quest_b, quest_c]` and start the queue.
5. Show `quest_c` on the screen. The condition is true. The wait ends. The result has `matchedImageId: "quest_c"`.
6. Use `{{quests}}` in the image of a `primitiveTap` step and save. The save is refused with the parameter name and the field path.

Run the tests:

- Domain: `dotnet test` on the Domain test project, filter `ImageList`.
- Service: `dotnet test` on the Service test project, filter `ImageList`.
- Web UI (if changed): `npm test` in `src/web-ui`.
