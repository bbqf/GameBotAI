# Quickstart: API Version and Gamepack Compatibility

1. Read the version:

   ```powershell
   Invoke-RestMethod -Uri "http://localhost:8080/api/version"
   ```

   Expect `major 1`, `minor 0`, `patch 0`, `schemaFormat 1` (add the API token header if the service needs one).

2. Dry run a gamepack for another major version. Save this file as `C:\temp\wrong.json`:

   ```json
   { "manifest": { "name": "demo", "version": "1", "requiredApiMajor": 2 } }
   ```

   Post it to `http://localhost:8080/api/gamepacks/install?dryRun=true`. Expect 200 with `valid: false` and a structure
   problem on `manifest` that names version 2 and version 1.

3. Install the same file without `dryRun`. Expect 422 and the same problem. `GET /api/gamepacks` does not list `demo`.

4. Change `requiredApiMajor` to 1, or remove it. Install again. Expect 200 and `demo` in the list.

5. Run the tests:

   ```powershell
   & "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~ApiVersion"
   ```
