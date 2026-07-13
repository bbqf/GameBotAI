# Implementation Plan: OCR-Parsed Dynamic Reschedule Offset

**Branch**: `068-reschedule-ocr-offset` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/068-reschedule-ocr-offset/spec.md`

## Summary

Extend the `reschedule-self` action (feature 065) so a Timer reschedule can derive its relative offset at runtime by OCR-reading an on-screen countdown timer, instead of only a static offset. Driving case: PNS Radio Quiz self-pacing off its growing cooldown. Failures fall back to a required static offset; parsed values are bounds-checked; the offset source is recorded in the execution log.

## Technical Context

**Language/Version**: C# / .NET (existing solution)
**Primary dependencies**: existing OCR (`ITextOcr` via `DynamicTextOcr` → `TesseractProcessOcr`/`EnvTextOcr`), existing self-reschedule (`ISelfRescheduleCoordinator`), existing emulator screen-capture used for image-match detection.
**Storage**: N/A (payload stored in the existing sequence action dictionary; no schema migration).
**Testing**: xUnit — mirror feature 065's `SelfReschedulePayloadTests`, `SelfRescheduleRunIntegrationTests`, `SelfRescheduleActionContractTests`.
**Project Type**: existing multi-project solution (Domain + Service + web-ui).
**Constraints**: MUST NOT change behavior when OCR-offset is absent (FR-008). MUST never fail the step or skip the reschedule on OCR error (FR-005). Bounds-clamp bad reads (FR-006).
**Scale/Scope**: single action type; ~4 production files + parser + tests.

## Constitution Check

- **Additive & backward-compatible**: new payload fields optional; absent → identical behavior (FR-008). PASS.
- **TDD**: tests authored before/with implementation. PASS (Phase 2).
- **Separation of concerns**: pure parse/validation in Domain; screen capture + OCR + scheduling side-effects in Service. PASS.
- **Deterministic tests**: OCR + screen capture behind interfaces so dispatch tests use fakes; time via existing `TimeProvider`. PASS.

No violations; no Complexity Tracking needed.

## Design

### Data model (see data-model.md)

Add an optional **OCR-offset spec** to the `reschedule-self` payload, valid only with `option=Timer`. Nested object under key `ocrOffset` in the action `parameters`:

```
"ocrOffset": {
  "region":   { "x": int, "y": int, "width": int, "height": int },  // captured-screen pixels
  "fallback": "HH:mm:ss",     // REQUIRED static offset used on any failure
  "min":      "HH:mm:ss",     // optional, default 00:00:01
  "max":      "HH:mm:ss"      // optional, default 24:00:00
}
```

Existing `timerRelativeOffset` / `timerTimeOfDay` remain. When `ocrOffset` is present it takes precedence for computing the offset, with `fallback` as the safety net; `timerRelativeOffset` need not be set.

### Duration parsing (Domain, pure)

New pure helper `CooldownDurationParser.TryParse(string ocrText, out TimeSpan value)`:
- Extract the first `HH:mm:ss` or `mm:ss` token from noisy OCR text via a tolerant regex (digits + colons; ignore surrounding non-`[0-9:]`; optional whitespace).
- 3 groups → `h:m:s`; 2 groups → `m:s`.
- Normalize safe OCR digit confusions (e.g. `O`→`0`) before matching.
- Return false on no match / non-numeric / overflow.

### Payload reader/validation (Domain)

- `SelfReschedulePayload`: add nullable typed `OcrOffset` (`Region`, `Fallback`, `Min`, `Max`) + `HasOcrOffset`; parse in `TryRead` (parse-level errors on malformed region/durations).
- `ActionPayloadValidationService`: cross-field rules — `ocrOffset` only with `option=Timer`; `region` present with positive width/height; `fallback` present/parseable; `min < max`; clear messages (mirror existing reschedule validation). Keep `SequenceStepValidationService` + `FileSequenceRepository.ValidateActionPayloads` consistent (same allow-list pattern as prior action-payload changes).

### Dispatch resolution (Service — the only behavioral change)

In `SequenceExecutionService.DispatchSelfReschedule` (make it receive `sessionId`; `DispatchActionAsync` already has it):
1. If `payload.HasOcrOffset`:
   - Capture the current emulator frame for `sessionId` (reuse the screen-capture service image-match detection already uses).
   - Crop `region`, `ITextOcr.Recognize`, `CooldownDurationParser.TryParse`.
   - parsed AND within `[min,max]` → `effectiveOffset = parsed`, source `ocr`; else `effectiveOffset = fallback`, source `fallback` (+ reason: no-capture / ocr-empty / parse-failed / out-of-bounds).
   - `ScheduleSelf(..., timerTimeOfDay: null, timerRelativeOffset: effectiveOffset)`.
   - Encode source + recognized text + duration into `ActionDispatchResult.Message` (execution log, FR-007).
2. Else: unchanged path.

Introduce a small `IOcrOffsetResolver` (Service) wrapping screen-capture + OCR + parser so dispatch stays testable with fakes; Tesseract/screen wiring stays in composition root.

### Phase 0 — research (research.md)

Confirm during implementation (low risk): exact screen-capture service/method to get a frame by `sessionId` in the Service layer + the crop pattern in `TextMatchEvaluator`; OCR PSM suitability for a short digits/colons timer (config, not code).

### Phase 1 — contracts/quickstart

No external HTTP contract change (action `parameters` is a free-form dict already accepted). Add an example payload + `quickstart.md` showing a Radio-Quiz-style `reschedule-self` with `ocrOffset`.

## Phase 2 — Test plan (write first)

- **Domain unit**: `SelfReschedulePayload` parses `ocrOffset` (valid + malformed); `ActionPayloadValidationService` accepts valid, rejects non-Timer option / missing region / non-positive region / missing fallback / min≥max.
- **Domain unit (parser)**: `mm:ss`, `hh:mm:ss`, noisy text, `O`→`0`, zero, garbage, overflow.
- **Service/integration**: fake OCR good timer → next firing `now + parsed`, source `ocr`; fake OCR empty/garbage → `now + fallback`, source `fallback`; out-of-bounds → fallback; absent ocrOffset → unchanged (regression).
- **Contract**: action payload round-trips `ocrOffset`.

## Progress Tracking

- [x] Spec written & validated
- [x] Plan written
- [ ] data-model.md, quickstart.md
- [ ] tasks.md (via `/speckit-tasks`)
- [ ] Implementation (TDD) + green build/tests
