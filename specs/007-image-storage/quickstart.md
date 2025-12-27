# Quickstart: Disk-backed Reference Images

## Prerequisites

- Build and run the service (`dotnet run -c Debug --project src/GameBot.Service`).
- Ensure `data/images` directory exists (service will create if missing).

## Upload an image

POST `/images` with JSON:

```json
{
  "id": "Home",
  "data": "<base64>"
}
```

Expect `201 Created` with `{ "id": "Home" }`.

## Verify and use

- GET `/images/Home` → `200 OK`.
- Create an `image-match` trigger with `referenceImageId: "Home"`.
- Test the trigger with `/triggers/{id}/test`.

## Overwrite and delete

- POST `/images` again with same `id` overwrites file atomically.
- DELETE `/images/Home` → `204 No Content`.

## Restart persistence check

1. Upload `Home` as above.
2. Restart the service.
3. GET `/images/Home` → `200 OK` without re-upload.