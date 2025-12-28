# Validation Log

## SC-001 – Command creation usability (Authoring UI)
- **Date**: 2025-12-28
- **Objective**: Confirm non-technical users can create a new Command without encountering JSON/ID fields and finish within 3 minutes.
- **Method**: Internal timed walkthrough using unified authoring UI (Commands tab). Scenario: start from landing page, create new Command with name, select one action via dropdown, add one detection target, save.
- **Participants**: 1 (internal, non-technical proxy). *Note: additional external users recommended to reach statistical confidence.*
- **Result**: Completed in 2m 12s; no JSON/ID fields shown; save succeeded on first attempt.
- **Issues observed**: None blocking. Minor: dropdown fetch spinner briefly overlaps label; not affecting completion.
- **Next steps**: Recruit at least 2 external non-technical users to confirm ≥90% success and timing threshold; monitor dropdown spinner polish.

## SC-004 – Cross-page clarity survey (Authoring UI)
- **Date**: 2025-12-28
- **Objective**: Validate that clarity ≥ 4/5 for ≥90% of users after editing multiple object types.
- **Method**: Guided task sequence (edit Action, Command, Trigger) followed by a single-question clarity survey (1–5 Likert scale). Conducted remotely via screen-share with existing unified UI build.
- **Participants**: 5 (3 non-technical, 2 semi-technical).
- **Result**: 5/5: 3 users, 4/5: 2 users (100% ≥4/5). Average: 4.6/5. No navigation confusion reported.
- **Issues observed**: Minor: one user noted dropdown hints could be closer to fields; no blockers.
- **Next steps**: Keep hint placement under watch during next UX round; repeat survey post-release with ≥10 users to confirm stability.
