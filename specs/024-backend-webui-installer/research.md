# Research

## Decision 1: Prerequisite acquisition strategy
- Decision: Use a hybrid prerequisite strategy where the installer first checks local machine state, then installs from bundled offline payloads when available, and falls back to controlled online acquisition only for missing components not bundled.
- Rationale: Satisfies requirement for self-contained installation while still supporting smaller installer packages and patchable prerequisite delivery.
- Alternatives considered:
  - Online-only prerequisite install (rejected: fragile in restricted enterprise/offline environments).
  - Fully bundled-only with no online fallback (rejected: larger package footprint and slower update cadence for dependency patches).

## Decision 2: Windows runtime mode registration
- Decision: Implement two explicit registration paths: (1) service mode with elevation-required Windows Service registration and boot auto-start, (2) background-application mode registered for optional user-login startup without admin rights.
- Rationale: Directly matches clarified behavior and separates privilege-sensitive operations from user-space runtime behavior.
- Alternatives considered:
  - Single-mode deployment with optional service conversion later (rejected: does not satisfy mode-selection requirement during install).
  - Scheduled task for both modes (rejected: less clear operational model and less aligned with service semantics).

## Decision 3: Default network exposure and firewall policy
- Decision: Bind backend to non-loopback interfaces by default and apply installer-managed firewall rules scoped to private/local network ranges when privileges permit; if firewall changes cannot be applied, require explicit operator confirmation before continuing.
- Rationale: Balances reachability with safer default exposure while preserving install continuity in policy-constrained environments.
- Alternatives considered:
  - Fully open inbound exposure by default (rejected: higher security risk).
  - Localhost-only default (rejected: conflicts with explicit requirement for real interface reachability).

## Decision 4: Endpoint protocol default and TLS path
- Decision: Default announced endpoints to HTTP within private-network scope; provide optional HTTPS setup during install that validates certificate inputs before finalizing secure endpoints.
- Rationale: Delivers reliable first-run experience while allowing secure transport hardening when certificate material is available.
- Alternatives considered:
  - HTTPS-only mandatory default (rejected: high install failure risk without certificate pre-provisioning).
  - Defer HTTPS entirely to post-install manual steps (rejected: weakens guided secure deployment posture).

## Decision 5: Deterministic Web UI port resolution
- Decision: Resolve Web UI port using fixed preference order `8080 -> 8088 -> 8888 -> 80`; choose the first available unless the operator explicitly overrides.
- Rationale: Produces deterministic behavior for automation and aligns with clarified standard-port preference while avoiding privileged-port first selection.
- Alternatives considered:
  - Lowest available preferred port (rejected: less predictable across hosts).
  - Start with port 80 (rejected: frequently blocked by privileges/policies in non-admin contexts).

## Decision 6: Port conflict detection and suggestions
- Decision: Add a preflight listener/occupancy check for backend and web ports and return at least one valid alternative suggestion per conflict before committing configuration.
- Rationale: Prevents post-install startup failures and supports unattended installs through deterministic fallback suggestions.
- Alternatives considered:
  - Detect conflicts only at runtime start (rejected: late failure and poorer operator experience).
  - Random ephemeral fallback port selection (rejected: poor discoverability and inconsistent endpoint announcements).

## Decision 7: Unattended CLI contract
- Decision: Expose every configurable install parameter as explicit CLI switches, validate all arguments before system mutations, and return actionable non-zero errors with short remediation text.
- Rationale: Meets unattended installation requirement and supports script-friendly deployment in CI/IT automation pipelines.
- Alternatives considered:
  - Partial CLI coverage with interactive fallback prompts (rejected: breaks strict no-UI automation expectation).
  - Single JSON blob argument only (rejected: less discoverable and harder for ops scripts to maintain).

## Decision 8: Configuration persistence model
- Decision: Persist installation mode, endpoint settings, startup policy, and selected protocol in existing file-based configuration under `data/config` with explicit schema version marker.
- Rationale: Reuses established repository patterns and ensures restart-stable behavior without adding new storage systems.
- Alternatives considered:
  - Windows registry persistence only (rejected: weaker portability and less consistency with existing app config patterns).
  - New dedicated database/file store (rejected: unnecessary complexity for installer scope).

## Decision 9: Observability for installer runs
- Decision: Emit structured installer logs for prerequisite checks, port scans, mode registration actions, and final endpoint announcements, including warning paths when security-relevant operations are skipped.
- Rationale: Supports debugging, supportability, and constitution-aligned actionable error messaging.
- Alternatives considered:
  - Minimal textual logging only (rejected: low diagnosability).
  - Verbose debug logging by default (rejected: noisy and less operator-friendly in standard runs).
