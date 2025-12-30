# Data Model: Connect to game action

## Entities

### ConnectToGameAction
- id: string (existing action id)
- name: string (existing action display)
- type: enum "connect-to-game" (new)
- gameId: string (required; references Game)
- adbSerial: string (required; manual or suggested)

### SessionContext
- sessionId: string (required)
- gameId: string (required; matches ConnectToGameAction)
- adbSerial: string (required; matches ConnectToGameAction)
- createdAt: datetime (for freshness decisions)

### Game (existing)
- id: string
- name: string
- other game metadata (unchanged)

## Relationships
- ConnectToGameAction -> Game: required reference by gameId.
- SessionContext is produced by executing ConnectToGameAction and keyed by gameId + adbSerial for reuse.

## Validation Rules
- ConnectToGameAction must include a valid existing gameId.
- ConnectToGameAction must include a non-empty adbSerial.
- SessionContext must only be reused when both gameId and adbSerial match the target command context.

## State Notes
- Latest SessionContext per (gameId, adbSerial) overwrites prior cached value when a new connect action succeeds.
