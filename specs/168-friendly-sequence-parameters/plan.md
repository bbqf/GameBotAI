# Implementation Plan: Friendly Sequence Parameters

**Branch**: `168-friendly-sequence-parameters` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/168-friendly-sequence-parameters/spec.md`

## Summary

Add an optional "friendly" layer to parameter declarations of commands and sequences (feature 078).
A declaration gets a label, help text, an input kind (text, number, choice list, image), and kind
settings (options, allowed images, range, unit, pattern). The web UI uses these fields to show a better editor and
a better value form. A saved binding keeps the technical value, so the run path stays the same.
The only run-time change is the number pattern: the scope puts the entered number into the pattern
when a name resolves. All new fields are optional, so old data loads and runs with no change.

## Technical Context

**Language/Version**: C# on .NET (GameBot.Domain, GameBot.Service); TypeScript 5 and React 18 (src/web-ui)
**Primary Dependencies**: ASP.NET Core minimal API; Vite; Jest and Testing Library; Playwright (e2e); no new packages
**Storage**: Existing JSON files for commands and sequences (new members are optional and omitted when empty); no new store
**Testing**: xUnit in `tests/unit`, `tests/contract`, `tests/integration`; Jest in `src/web-ui`
**Target Platform**: Windows service host, browser UI (desktop and phone layout)
**Project Type**: Web application (service and web UI)
**Performance Goals**: one name resolution with a number pattern takes less than 1 µs (spec SC-008; benchmark in tasks.md); no change to the p95 step dispatch time. The plan sets no timing goal for the UI; thumbnails load on demand
**Constraints**: No migration; old API clients that send technical values keep working; the form never shows the technical value
**Scale/Scope**: 4 input kinds; at most 50 options per list; about 25 changed or new UI files; about 12 changed or new service and domain files

## Constitution Check

*GATE: Passed before Phase 0 research. Passed again after Phase 1 design.*

| Gate | Result |
|------|--------|
| I. Code quality | Pass. Small, cohesive types. No underscores in method names. Public members get XML docs. |
| II. Testing | Pass. Each rule gets a unit test. The API gets contract tests. The UI gets Jest tests. Coverage target is 80% line and 70% branch for touched files. |
| III. UX consistency | Pass. Each message names the problem and one way to fix it (spec FR-012). Phone level is **reduced** (see below). |
| IV. Performance | Pass. Goals are in Technical Context. The only hot-path change is one pattern replace per resolved name. A benchmark task and a perf note in the PR cover it. |
| V. Living docs | Pass. `docs/architecture.md`, `docs/mobile-ui-principles.md`, and `specs/STATUS.md` change in the same PR. Spec 078 gets "Implemented (iterated by 168)". |
| VI. STE | Pass. All new text follows STE. |
| Version bump | Task in tasks.md: raise `patch` in `installer/versioning/version.override.json` before the PR. |

*Phone check*: Phone support level is **reduced**, as the spec states. The value form uses touch-size
controls and has no horizontal scroll at 375 px. The declaration editor shows "Use a larger screen" on a
phone. Today no phone screen lets a person edit queue-template values (docs/mobile-ui-principles.md,
"Queue activity on a phone"). So this feature adds no new phone screen. It makes the shared form
phone-ready, and the first phone screen that hosts values can reuse it. See research.md, decision 7.

## Project Structure

### Documentation (this feature)

```text
specs/168-friendly-sequence-parameters/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── parameter-declaration.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Parameters/
│   ├── ParameterDeclaration.cs        # new optional members
│   ├── ParameterInputKind.cs          # new enum
│   ├── ParameterOption.cs             # new type
│   ├── ParameterNameRules.cs          # declaration rules for kinds
│   ├── ParameterValueRules.cs         # new: check a value against a declaration
│   └── ParameterScope.cs              # apply number pattern on resolve
└── Services/ParameterValidationService.cs   # value checks for entries and step bindings

src/GameBot.Service/
├── Models/Commands.cs                 # ParameterDeclarationDto, ParameterOptionDto
├── Endpoints/ParameterDtoMapper.cs    # map new members
├── Endpoints/ImageReferencesEndpoints.cs    # no change to the list (owner already present)
└── Swagger/SwaggerConfig.cs           # schema text for new members

src/web-ui/src/
├── components/stepthrough/ValuesForm.tsx   # use the shared value form
├── components/parameters/
│   ├── types.ts                       # new types and helpers
│   ├── ParameterDeclarationList.tsx   # card editor and live preview
│   ├── ParameterDeclarationCard.tsx   # new: one card
│   ├── ParameterBindingForm.tsx       # use the field components
│   ├── fields/                        # new: ChoiceField, ImagePickerField, NumberField, TextField
│   ├── AllowedImagesEditor.tsx        # new: author chooses the allowed images
│   ├── ParameterPassThrough.tsx       # new: "Use a parameter of this sequence" control
│   ├── ParameterFormPreview.tsx       # new: live preview in the editor
│   ├── OptionsEditor.tsx              # new: author edits choice options
│   ├── NumberSettings.tsx             # new: min, max, unit, pattern
│   ├── useImageThumbnail.ts           # new: lazy thumbnail loading
│   ├── suggestParameterName.ts        # new: name from label
│   └── parameterValueRules.ts         # new: client checks (the service is the authority)
├── services/images.ts                 # list with owners
└── styles                             # phone-size rules for the form

tests/
├── unit/Parameters/                   # rules and pattern tests
├── contract/                          # declaration round trip and rejection tests
└── integration/                       # entry save with friendly values; run with a pattern
docs/ (architecture.md, mobile-ui-principles.md), specs/STATUS.md, specs/078-sequence-parameters/spec.md, CHANGELOG.md
```

**Structure Decision**: Web application. The change follows the layout of feature 078: one domain
model, one mapper shared by the three endpoint groups, and one set of React components under
`components/parameters`.

## Complexity Tracking

No constitution violation. Nothing to justify.
