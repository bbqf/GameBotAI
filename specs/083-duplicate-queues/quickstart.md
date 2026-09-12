# Quickstart: Duplicate Queues

## Backend (API)

1. Start the service (`dotnet run --project src/GameBot.Service`).
2. Create or pick an existing queue, note its `id` (`GET /api/queues`).
3. Duplicate it, resubmitting its own emulator serial unchanged (the web-ui pre-fills this from the
   source; a raw `curl` call must supply it explicitly since it's a required field):

   ```bash
   curl -X POST http://localhost:8080/api/queues/<id>/duplicate \
     -H "Content-Type: application/json" \
     -d '{"name":"<id> copy","emulatorSerial":"<source emulatorSerial>"}'
   ```

4. Expect `201 Created` with a new queue whose fields match the source except `id`/`name`/status
   history. Verify with `GET /api/queues/<newId>` — `entryCount` and `linkedTemplateId` should
   match the source.
5. Re-run the same call with the *same* name as the source queue and expect `400 invalid_request`.
6. Re-run with a *different* `emulatorSerial` (e.g. another connected device) and the same/a new
   name — expect `201 Created`, with the new queue bound to the requested emulator and the source
   queue's own emulator binding unchanged. Omitting `emulatorSerial` entirely is rejected with
   `400 invalid_request` ("emulatorSerial is required").

## Web UI

1. Start the web-ui dev server (`npm run dev` in `src/web-ui`) against the running service.
2. Open the Queues page.
3. Click **Duplicate** on any queue row.
4. Confirm the pre-filled name (`<source name> (copy)`) and the pre-filled emulator fields (source's
   serial/instance name/instance index); edit either as desired (the emulator, unlike the name, may
   be left as-is) and confirm.
5. Verify the new queue appears in the list with the same (or newly chosen) emulator, and the same
   template/game as the source, with status `Stopped`.
6. Verify the source queue row is unchanged, including its own emulator binding.

## Automated tests

- Backend: `dotnet test` — cases in `QueuesDuplicateEndpointTests.cs` cover success with an
  unchanged emulator (config + entries copied), success with a different emulator (new queue uses
  the requested emulator, source untouched), 404 on missing source, 400 on missing name, 400 on
  unchanged name, 400 on missing `emulatorSerial`.
- Web UI: `npm test` in `src/web-ui` — cases cover the Duplicate button rendering, modal default
  name and emulator pre-fill, submitting an unchanged vs. a changed emulator, and the
  success/error paths of the duplicate service call.
