# Quickstart: Duplicate Queues

## Backend (API)

1. Start the service (`dotnet run --project src/GameBot.Service`).
2. Create or pick an existing queue, note its `id` (`GET /api/queues`).
3. Duplicate it:

   ```bash
   curl -X POST http://localhost:8080/api/queues/<id>/duplicate \
     -H "Content-Type: application/json" \
     -d '{"name":"<id> copy"}'
   ```

4. Expect `201 Created` with a new queue whose fields match the source except `id`/`name`/status
   history. Verify with `GET /api/queues/<newId>` — `entryCount` and `linkedTemplateId` should
   match the source.
5. Re-run the same call with the *same* name as the source queue and expect `400 invalid_request`.

## Web UI

1. Start the web-ui dev server (`npm run dev` in `src/web-ui`) against the running service.
2. Open the Queues page.
3. Click **Duplicate** on any queue row.
4. Confirm the pre-filled name (`<source name> (copy)`), edit if desired, and confirm.
5. Verify the new queue appears in the list with the same emulator/template/game as the source and
   status `Stopped`.
6. Verify the source queue row is unchanged.

## Automated tests

- Backend: `dotnet test` — new cases in `QueuesEndpointsTests.cs` cover success (config + entries
  copied), 404 on missing source, 400 on missing name, 400 on unchanged name.
- Web UI: `npm test` in `src/web-ui` — new cases cover the Duplicate button rendering, modal
  default name, and the success/error paths of the duplicate service call.
