# Quickstart: Before-Each-Run Schedule Type

1. Build & test:
   - `dotnet build C:\src\GameBot\GameBot.sln -c Debug`
   - `dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~Queue"`
   - `dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~QueueTemplates"`
   - `dotnet test C:\src\GameBot\tests\integration --filter "FullyQualifiedName~QueueTemplates"`
   - web-ui: `npm --prefix C:\src\GameBot\src\web-ui run build` and `npm --prefix C:\src\GameBot\src\web-ui test -- schedulingAreas QueueSchedulingAreas`
2. Save a template with a Before Each Run entry:
   ```json
   { "name": "demo", "entries": [
     { "sequenceId": "<ensure-city>", "scheduleType": "BeforeEachRun" },
     { "sequenceId": "<task>", "scheduleType": "Timer", "timerRelativeOffset": "+10s" }
   ] }
   ```
3. `GET` the template: the first entry returns `"scheduleType": "BeforeEachRun"`.
4. Start a queue linked to it; after ~10 s the execution log shows `<ensure-city>` then `<task>`; the monitor lists `<ensure-city>` with reason "Before Each Run".
5. In the web editor the entry appears in the "Before each run" area; drag it to "After every step" and back, save, reload — the area round-trips.
