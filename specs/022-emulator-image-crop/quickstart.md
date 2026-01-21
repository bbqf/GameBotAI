# Quickstart: Emulator Screenshot Cropping

1. Checkout branch `022-emulator-image-crop`.
2. Backend: from repo root run `dotnet build -c Debug` then `dotnet run -c Debug --project src/GameBot.Service`.
3. Frontend: in `src/web-ui`, run `npm install` if needed, then `npm run dev`.
4. From the authoring UI, open the emulator view, trigger screenshot capture, draw a rectangle (>=16x16), and save as PNG; confirm save path shown matches expectations.
5. For API validation, call `GET /emulator/screenshot` to fetch a PNG, then `POST /images/crop` with bounds and name; verify 201 response and stored file under `data/images`.
6. Run tests: `dotnet test -c Debug` and frontend tests via `npm test` / `npm run test:e2e` as applicable.
