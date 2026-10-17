# Quickstart: Gamepack image alternates

## Check the feature by hand

1. Make a gamepack file with two images, `img-a` and `img-b`. Add `"alternates": ["img-b"]` to `img-a`.
2. Run the dry run for the file. The result is valid.
3. Install the file. Read the alternates of `img-a` (for example `GET /api/images/img-a/alternates`). The list is `["img-b"]`.
4. Add `"bogusMember": 1` to `img-b`. Run the dry run. The result is not valid. The message names `img-b` and `bogusMember`.
5. Try the install. It is refused. No object is created.
6. Set `"alternates": ["img-a"]` on `img-a`. Run the dry run. The message says that an image cannot be its own alternate.
7. Set `"alternates": ["missing-image"]`. Run the dry run. The message names `img-a` and `missing-image`, and it has the hint of the alternates API.
8. Take over a user image `img-a` that has the alternates list `["img-c"]`. Install a gamepack that declares `img-a` with a different list. Restore the take-over backup. The list of `img-a` is `["img-c"]` again.

## Run the tests

```powershell
& dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Gamepack"
```

Check the project file name in `tests\unit` before you run the command.

## Test list

- `GamepackFileSerializerImageMembersTests`: unknown member, case-insensitive known members, wrong type, non-text item.
- `GamepackValidatorImageAlternatesTests`: valid list, same-file target, stored target, self, unknown, duplicate, more than 8, target removed by the install.
- `GamepackApplierImageAlternatesTests`: list stored, re-install with same bytes, absent member keeps the list, empty list clears it, undo on failure, refused install creates nothing, take-over backup holds the old list, restore and undo put it back, capture of the live content.
