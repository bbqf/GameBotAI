# Quickstart: API Structure Cleanup

## Setup
1. Install .NET 9 SDK and Node (for web-ui, not required for routing change tests).
2. From repo root: `dotnet restore`.

## Build and Test
1. Build: `dotnet build -c Debug`.
2. Tests: `dotnet test -c Debug` (ensures contract/integration suites pass using `/api` routes only).
3. Optional: Run service locally: `dotnet run -c Debug --project src/GameBot.Service`.

## What to Verify
- All public endpoints respond only under `/api/{resource}`; legacy roots return non-success with guidance.
- Swagger shows grouped sections: Actions, Sequences, Sessions, Configuration, Triggers (if present).
- Each documented endpoint includes request/response schemas and at least one example payload.
- Contract and integration tests reference only canonical `/api` routes and pass.

## Notes
- No new dependencies or storage are introduced.
- Keep Windows-only guards for emulator/ADB code intact.
