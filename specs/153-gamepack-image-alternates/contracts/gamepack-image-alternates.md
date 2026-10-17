# Contract: Gamepack file image entry

This feature changes the file format and the take-over backup file (one new optional member). No endpoint is added or
changed. The dry run and install endpoints keep their current request and response shapes. The new problems use the
existing problem object. The OpenAPI text for the gamepack file lists `alternates` as an image member.

## Image entry (file format)

```json
{
  "id": "alliance-button-farm",
  "contentType": "image/png",
  "filename": "alliance-button-farm.png",
  "dataBase64": "iVBORw0KGgo...",
  "alternates": ["alliance-button-event"]
}
```

`alternates` is optional.

## Dry run and install responses

Existing problem object: `{ "category": "Structure", "type": "Image", "name": "<image id>", "message": "..." }`.

### Unknown member

Input: an image entry with `"bogusMember": 1`.

- Dry run: not valid. One problem. The message names the image id and `bogusMember`.
- Install: refused with the same problem. No object is created.

### Bad alternates list

Each message uses the message and the hint of `ImageAlternatesValidator`, and names the image id and the offending ids.
Form: `Image '<id>': <message> Ids: <offending ids>. <hint>`. The count message for more than 8 ids has no offending ids.
The `Ids:` part is omitted when the list of offending ids is empty.

| Input | Message of the validator | Offending ids named |
|-------|--------------------------|---------------------|
| List holds the id of the image | An image cannot be its own alternate. | The own id |
| List holds an id that is not in the file and not stored | Every alternate must be a stored image. | The unknown ids |
| List holds a repeated id | Alternates must not repeat an id. | The repeated ids |
| List has more than 8 ids | An image may have at most 8 alternates; N were given. | None |
| List holds an id with a bad form | Alternate ids must be alphanumeric/dash/underscore (1-128 chars). | The bad ids |
| `alternates` is not a list, or an item is not text | Existing "wrong shape" text. The image is named. | None |

## Take-over backup file

`TakeOverBackupImage` has a new optional member `alternates` (list of text). It holds the stored list of the live image
before the take-over. It is omitted when the list is empty. A restore puts the list back.

### Success

Dry run: valid. Install: `GET`-reading the alternates of the image (`IImageAlternatesRepository.GetAlternates`)
returns the declared ids in the declared order.

## Unchanged contracts

- `PUT /api/images/{id}/alternates`: request, response, and validation do not change.
- Image matching in detection: no change.
