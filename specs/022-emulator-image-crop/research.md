# Research: Emulator Screenshot Cropping

## Decisions and Rationale

- **Decision**: Capture emulator screenshots via existing GameBot emulator session/ADB integration (no new tools).
  - **Rationale**: Reuses established capture pipeline and permissions; avoids new external dependencies.
  - **Alternatives considered**: OS-level screen grabbers (adds permissions/complexity); third-party capture libraries (adds dependencies).

- **Decision**: Perform cropping in-process using existing System.Drawing/OpenCvSharp utilities; output PNG only.
  - **Rationale**: Keeps fidelity lossless, aligns with clarified PNG requirement and current tooling; no new libraries needed.
  - **Alternatives considered**: External CLI image tools (extra process/packaging); JPEG output (lossy, conflicts with requirement).

- **Decision**: Store cropped images under `data/images` with user-provided names; block silent overwrite and prompt to rename or replace.
  - **Rationale**: Aligns with existing file-backed storage and spec’s duplicate-name handling; keeps assets discoverable with predictable paths.
  - **Alternatives considered**: New storage location (splits assets); hash-based filenames (hurts human discoverability).

- **Decision**: Enforce minimum crop size 16x16 px and validate selection before save.
  - **Rationale**: Matches clarified requirement; prevents unusable assets and supports quick retries.
  - **Alternatives considered**: Smaller minimums (risk tiny unusable crops); larger minimums (blocks small UI elements).

- **Decision**: Target capture→crop→save p95 ≤1s on 1080p frames and limit deviation to ≤1px from selection.
  - **Rationale**: Keeps UX responsive during authoring; aligns with spec accuracy criteria.
  - **Alternatives considered**: No explicit target (risks regressions); stricter targets (<500ms) without evidence.
