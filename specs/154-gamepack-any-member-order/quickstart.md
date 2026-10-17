# Quickstart: Accept any member order in gamepack files

1. Build a pack with a sequence step that has a break condition. Write the condition as `{"imageId":"x","minSimilarity":0.9,"negate":false,"type":"imageVisible"}` (type last).
2. Send a dry run to the install route with `dryRun=true`. Expected: 200 and `valid:true`.
3. Send a real install. Expected: 200. The stored sequence equals the one from the "type first" file.
4. Remove the `type` member and send a dry run. Expected: 400, error code `InvalidJson`, and a problem with the JSON path of the condition.
5. Set `"type":"nope"`. Expected: 400, `InvalidJson`, and a problem with the JSON path.
6. Set `"type":5`, and then add a second `type` member. Expected: 400 and a JSON path for each.
7. Compute the content hash for both member orders. Expected: equal.

Run the tests:

`dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Gamepack"`

Test projects: `tests/unit`, `tests/contract`, `tests/integration`.
