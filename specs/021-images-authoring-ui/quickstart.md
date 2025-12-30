# Quickstart: Images Authoring UI

## Prerequisites
- Windows environment with .NET 9 SDK and Node.js installed.
- Existing GameBot data directory (includes data/images and triggers files).

## Backend
1. Restore and build: `dotnet build -c Debug` (repo root).
2. Run service: `dotnet run -c Debug --project src/GameBot.Service`.
3. Images endpoints used:
   - `GET /api/images` → list IDs
   - `GET /api/images/{id}` → fetch image content
   - `GET /api/images/{id}/metadata` → fetch metadata
   - `POST /api/images` (multipart with id, file) → create
   - `PUT /api/images/{id}` (multipart with file) → overwrite
   - `DELETE /api/images/{id}` → delete if unreferenced (409 when triggers reference)

## Frontend (authoring UI)
1. From `src/web-ui`: install deps if needed (`npm install`).
2. Run dev server: `npm run dev -- --host --port 4173`.
3. Navigate to the Images authoring section:
   - List page shows IDs only.
   - Click an ID to open detail with image preview.
   - Overwrite by uploading a new file for the same ID (PUT).
   - Delete from detail page; blocked when triggers reference the image.

## Validation
- Upload only png/jpg/jpeg ≤10 MB; expect clear validation errors otherwise.
- Detail load target: preview within 2s for ≤10 MB assets on standard network.
- Overwrite target: new preview visible within 5s post-submit.
- Delete behavior: 409 with blocking trigger IDs when in use; 204 when unreferenced.
