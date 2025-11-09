# Data Model: Triggered Profile Execution

## Entities

### AutomationProfile (extended)
- id: string
- name: string
- description: string
- steps: array<InputAction>
- triggers: array<ProfileTrigger>

### ProfileTrigger
- id: string (unique per profile)
- type: enum { delay, schedule, image-match, text-match }
- enabled: bool
- cooldownSeconds: int (≥0, default 60)
- lastFiredAt: timestamp | null
- lastEvaluatedAt: timestamp | null
- lastResult: TriggerEvaluationResult | null
- params: TriggerParams (one-of based on type)

### TriggerParams (one-of)
1. DelayParams
   - seconds: int (>0)
2. ScheduleParams
   - timestamp: ISO 8601 (future date/time)
3. ImageMatchParams
   - referenceImageId: string (identifier for stored image asset)
   - region: Region
   - similarityThreshold: float [0..1] (default 0.85)
4. TextMatchParams
   - target: string (exact or regex)
   - region: Region
   - confidenceThreshold: float [0..1] (default 0.80)
   - mode: enum { found, not-found }

### Region
- x: float [0..1]
- y: float [0..1]
- width: float (0 < width ≤ 1, x + width ≤ 1)
- height: float (0 < height ≤ 1, y + height ≤ 1)

### TriggerEvaluationResult
- status: enum { pending, satisfied, cooldown, disabled }
- similarity: float | null (image)
- confidence: float | null (text)
- reason: string (human-readable summary)
- evaluatedAt: timestamp

## Relationships
- AutomationProfile 1..* ProfileTrigger (triggers owned by profile)
- ProfileTrigger -> Region (composition for spatial triggers)
- ProfileTrigger -> TriggerParams (polymorphic)

## Validation Rules
- DelayParams.seconds > 0
- ScheduleParams.timestamp must be > now at creation; enabling past timestamp invalid
- ImageMatchParams.referenceImageId not empty
- ImageMatchParams.similarityThreshold in [0.5..1] (reject lower values to reduce false positives)
- TextMatchParams.confidenceThreshold in [0.5..1]
- Region coordinates normalized and within bounds; width/height > 0
- cooldownSeconds ≥ 0
- Trigger type-specific params present exactly once
- Enabled trigger with invalid params must fail validation and remain disabled

## State Transitions
1. disabled → enabled (validation passes)
2. enabled + condition unmet → pending
3. enabled + condition met (first evaluation) → satisfied → cooldown (immediately after firing)
4. cooldown + cooldown elapsed → pending
5. enabled + schedule time passed without firing (because disabled) → remains disabled (no retroactive firing)

## Persistence
- Triggers embedded inside profile JSON under `triggers` key.
- lastFiredAt, lastEvaluatedAt, lastResult updated atomically post evaluation cycle.

## Derived Fields (runtime only, not persisted when null)
- nextEligibleAt: timestamp computed = lastFiredAt + cooldownSeconds (if lastFiredAt not null)

## Notes
- Future extension: add trigger "pixel-color" or "network-event" without altering existing structures by introducing new params variant.
