# Data Model

## Entities

### Command
- commandId: string (existing identifier)
- name: string
- detection: DetectionConfig (required)
- otherFields: existing command attributes (reuse current schema)

### DetectionConfig
- detectionTargetId: string (required)
- parameters: object/dictionary (required; stored per target schema)
- lastUpdatedUtc: datetime

### SessionCache
- sessionId: string (required)
- gameId: string (required)
- emulatorId: string (required)
- startedAtUtc: datetime
- lastSeenAtUtc: datetime
- status: enum [running, stopping, stopped, stale]
- source: string (e.g., "start-session")

### RunningSession
- sessionId: string (required; unique)
- gameId: string (required)
- emulatorId: string (required)
- startedAtUtc: datetime
- lastHeartbeatUtc: datetime
- status: enum [running, stopping]

### RunningSessionsList (view model)
- sessions: list<RunningSession>
- updatedAtUtc: datetime

## Relationships and Rules
- Each command references exactly one DetectionConfig.
- DetectionConfig is persisted within the command JSON (data/commands) and must be rehydrated on edit.
- At most one RunningSession exists per (gameId, emulatorId); starting a new session replaces the prior one and removes its entry even if the stop call fails.
- SessionCache mirrors the latest started session and is used by execution flows when no sessionId is provided explicitly.
- RunningSessionsList is derived from service-held running sessions; UI polls for updates.
