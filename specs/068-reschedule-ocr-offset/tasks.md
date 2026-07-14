# Tasks: OCR-Parsed Dynamic Reschedule Offset (068)

Implemented TDD-first following plan.md. All tasks complete; `dotnet build GameBot.sln` clean,
tests green (Unit 605 / Contract 91 / Integration 287).

## Phase 1 — Domain (pure)

- [x] T001 Test: `CooldownDurationParserTests` — hh:mm:ss, mm:ss, noisy text, O→0 / l→1, zero, garbage, overflow, first-token.
- [x] T002 Impl: `CooldownDurationParser.TryParse(string, out TimeSpan)` — tolerant regex + digit normalization + checked overflow guard.
- [x] T003 Test: `SelfReschedulePayloadTests` ocrOffset cases — defaults, explicit bounds, missing region, missing/malformed fallback, absent-unchanged.
- [x] T004 Impl: `SelfRescheduleOcrOffset` + `OcrOffsetRegion`; `SelfReschedulePayload` parses nullable `OcrOffset` (+`HasOcrOffset`) from JsonElement or dict.

## Phase 2 — Validation

- [x] T005 Test: `OcrOffsetValidationTests` — valid Timer passes; non-Timer rejected; non-positive region; min≥max; missing fallback; Timer+ocrOffset needs no static field.
- [x] T006 Impl: `SequenceStepValidationService.ValidateRescheduleSelfPayload` — ocrOffset cross-field rules; relax static-timer-field requirement when ocrOffset present.

## Phase 3 — Service (behavioral change)

- [x] T007 Test: `OcrOffsetResolverTests` — good read → ocr; empty/garbage/zero/above-max/no-capture/no-session/ocr-error/off-frame → fallback with reason; mm:ss read.
- [x] T008 Impl: `IOcrOffsetResolver` / `OcrOffsetResolution` / `OcrOffsetSource`; `OcrOffsetResolver` (capture→crop→OCR→parse→bounds, never throws); `StaticFallbackOcrOffsetResolver`; `ISessionFrameSource` + `BackgroundCaptureSessionFrameSource`.
- [x] T009 Impl: `SequenceExecutionService.DispatchSelfReschedule` receives `sessionId`, runs the OCR path when `HasOcrOffset` + Timer, schedules with the effective offset, and encodes source/text/duration into the log message (FR-007).
- [x] T010 Impl: DI wiring in `GameBotServiceSetup` — real resolver on Windows+ADB, static fallback otherwise (keeps the ADB-disabled test graph buildable).

## Phase 4 — Contract & regression

- [x] T011 Test: `SelfRescheduleActionContractTests` — ocrOffset round-trips; non-Timer ocrOffset rejected (400); missing fallback rejected (400).
- [x] T012 Regression (FR-008): existing feature-065 payload / contract / integration tests pass unchanged with ocrOffset absent.
