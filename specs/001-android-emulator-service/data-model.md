# Data Model: GameBot Android Emulator Service

Date: 2025-11-05
Branch: 001-android-emulator-service

## Entities

### EmulatorSession
- id: string (UUID)
- gameId: string
- status: enum [creating, running, stopping, stopped, failed]
- startTime: datetime
- uptime: duration
- health: enum [ok, degraded, error]
- capacitySlot: int
- authContext: string (token id masked)

### GameArtifact
- id: string (UUID)
- title: string
- hash: string (checksum)
- path: string (absolute or repo-relative)
- region: string (optional)
- notes: string (optional)
- complianceAttestation: boolean

### AutomationProfile
- id: string (UUID)
- name: string
- gameId: string
- steps: InputAction[]
- checkpoints: string[] (labels of expected states)

### InputAction
- type: enum [key, tap, swipe]
- args: object (keyCode | {x,y} | {x1,y1,x2,y2,durationMs})
- delayMs: int (delay before execution)
- durationMs: int (optional)

### Snapshot
- id: string (UUID)
- sessionId: string
- timestamp: datetime
- contentType: string (e.g., image/png)
- bytes: binary (not persisted by default; streamed on request)

### AuthToken
- id: string (UUID)
- name: string
- tokenHash: string
- scopes: string[]
- createdAt: datetime
- lastUsedAt: datetime (optional)

## Relationships
- EmulatorSession references one GameArtifact; optionally one AutomationProfile at start.
- AutomationProfile is authored for a GameArtifact but can be shared if compatible.
- Snapshots belong to an EmulatorSession.
- AuthToken authorizes requests and is not exposed in logs.
