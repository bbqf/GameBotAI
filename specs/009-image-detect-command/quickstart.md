# Quickstart: Sample Detect Command

## How to run the sample detection command locally

### 1. Start the service

```powershell
dotnet run -c Debug --project src/GameBot.Service
```

### 2. Create a session

Create a session to get your session ID:

```bash
curl -X POST "http://localhost:5273/sessions" -H "Content-Type: application/json" -d '{"gameId":"your-game-id"}'
```

The response will include an `id` field — this is your session ID.

### 3. Upload your PNG as `home_button` and force-execute the sample

**Option A: Using the helper script**

```powershell
.\scripts\sample-run-detect.ps1 -SessionId <your-session-id> -ImagePath C:\path\to\home_button.png
```

Replace `<your-session-id>` with the session ID from step 2, and `C:\path\to\home_button.png` with the path to your reference image.

**Option B: Using curl to force-execute directly**

The sample command is preloaded with ID `00000000000000000000000000000001` (see `data/commands/sample-detect-command.json` if present):

```bash
curl -X POST "http://localhost:5273/commands/00000000000000000000000000000001/force-execute?sessionId=<your-session-id>"
```

### Prerequisites

- Windows
- Service running on port 5273 (or override with `$env:ASPNETCORE_URLS="http://localhost:5273"` before starting)
- `GAMEBOT_USE_ADB=false` (stub screen source) or an active emulator session for screenshots
- For stub mode, you can also set `GAMEBOT_TEST_SCREEN_IMAGE_B64` to provide a deterministic screenshot

## Notes

- Selection strategy:
	- `HighestConfidence` (default): choose the match with the greatest confidence.
	- `FirstMatch`: choose the first detected match.
- With `confidence >= 0.99`, adapter caps results to one match and populates tap `x`/`y` with the center + offsets.
- If detection services are unavailable, coordinates are skipped gracefully and existing `args` are used.

### Sample detection JSON

```json
{
	"id": "00000000000000000000000000000001",
	"name": "Sample Detection Command",
	"steps": [
		{ "type": "Action", "targetId": "d6bfccf500000000000000000000001", "order": 0 }
	],
	"detection": {
		"referenceImageId": "home_button",
		"confidence": 0.99,
		"offsetX": 0,
		"offsetY": 0,
		"selectionStrategy": "HighestConfidence"
	}
}
```
