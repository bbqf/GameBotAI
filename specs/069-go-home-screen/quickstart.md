# Quickstart: Go To Home Screen Action

## What it does

Adds a `go-to-home-screen` action that presses the Android HOME button so the device returns to its
main/home screen, leaving the game running in the background. It is the leave-game counterpart to
`connect-to-game`, wired like `ensure-game-running`.

## Use it in a sequence

Add a step whose action is:

```json
{ "stepId": "leave-game", "action": { "type": "go-to-home-screen", "parameters": {} } }
```

Run the sequence against a running session — the device returns to the home screen; re-entering the
game resumes it (the process was not killed).

## Use it in a command

```json
{ "name": "Go Home", "steps": [ { "type": "GoToHomeScreen", "order": 0 } ] }
```

## Use it in the web-ui

Command editor → **Action type** → **Go to Home Screen** → **Add**. No fields to fill in.

## Build & test (real green gate)

Backend:

```powershell
& dotnet build "C:\src\GameBot\GameBot.sln"
& dotnet test  "C:\src\GameBot\GameBot.sln"
```

Web-ui (lint/tsc have pre-existing failures — use build + jest as the gate):

```powershell
& npm --prefix "C:\src\GameBot\src\web-ui" run build
& npm --prefix "C:\src\GameBot\src\web-ui" test -- --watchAll=false
```

## Key files (see plan.md for the full list)

- `src/GameBot.Domain/Actions/ActionTypes.cs`, `PrimitiveActionTypes.cs`, `PrimitiveActionVariants.cs`
- `src/GameBot.Domain/Commands/CommandStep.cs`, `FileSequenceRepository.cs`
- `src/GameBot.Domain/Services/SequenceRunner.cs`
- `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, `CommandExecutor.cs`
- `src/GameBot.Service/Models/Commands.cs`, `Endpoints/CommandsEndpoints.cs`
- `src/web-ui/src/components/commands/ActionTypeSelector.tsx`, `GoToHomeScreenPanel.tsx`, `CommandForm.tsx`
- `src/mcp-server/src/tools/commands.ts`

## Gotchas

- **Two allow-lists**: `PrimitiveActionTypes.All` covers three service validators automatically, but
  `FileSequenceRepository.ValidateActionPayloads` has its own hard-coded set — update it too or
  saving a sequence with the action returns HTTP 500.
- **Type-first JSON**: in polymorphic step objects the `type` property must be first.
- **HOME keycode 3** is already in `SessionManager.KeyNameMap`; no new ADB code is needed.
