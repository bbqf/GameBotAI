# Quickstart: Remove an installed gamepack record

The service listens on port 8080.

1. Install a pack.
   `POST /api/gamepacks/install` with a gamepack file.
2. Check the list.
   `GET /api/gamepacks` shows the pack.
3. Remove the record.
   `DELETE /api/gamepacks/demo-pack` gives `204`.
4. Check the list again.
   `GET /api/gamepacks` does not show `demo-pack`.
5. Send the same `DELETE` again. The answer is `404` with `result` `not_found`.
6. Install the same file again. The install is a first install. The list shows the version and `hasPreviousVersion` is false.

Content items of the pack (sequences, commands, images) stay after step 3. They belong to the user. If you install the same
name again, use `takeOver=true` to let the pack own them again.

## Test commands

- Build: `dotnet build C:\src\GameBot\GameBot.sln`
- Tests: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Gamepack"`
