# Contract: Catalog install UI

## Service function

`installFromCatalog(name: string, version: string, dryRun: boolean, takeOver: boolean): Promise<CatalogInstallResult>`

- Sends `POST /api/gamepacks/catalog/install` with the JSON body `{ name, version, dryRun, takeOver }`.
- `CatalogInstallResult` has the new optional field `takenOver?: number`.
- Error handling stays as it is (HTTP 422 gives `CatalogApiError`).

## Page controls

| Control | Label | Rule |
|---------|-------|------|
| Check box | `Take over objects of the user with the same name` | Off at load. Reuses the class `gamepacks-check`. |
| Hint | `The gamepack owns these objects and replaces them. The old content goes to a backup.` | Shown below the check box. |
| Button | `Install` or `Update` | With take-over on and Dry run off: enabled only after a valid dry run with the same version and take-over value. With Dry run on, the button starts a dry run and is enabled. |

## Messages

- Valid dry run, take-over on: `Dry run for {name} {version}: the install would work. Nothing changed. Take over {N}. Created ...`
- Take-over on, no matching dry run: the exact hint `Run the dry run with take-over first.` shows in the row of the version whose button is disabled.
- Real install result keeps the existing text.

- The count `Take over N.` shows in every dry run notice made with take-over on, valid or not. The conflict and problem lines stay visible.
- A change of the take-over check box clears the dry run result and the notice.
- A change of the Dry run check box keeps the last dry run result.
- Take-over off: the notice has no `Take over N.` text.

## Not changed

The API, the take-over rules, and the failure explanation text (issue 337).
