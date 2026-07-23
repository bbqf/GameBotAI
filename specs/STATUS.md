# Spec Status Index

This is the at-a-glance audit of every `specs/NNN-*` folder. Each spec's own `**Status**:`
line (just under its title) is the source of truth; this table is the roll-up.

**Specs are immutable per-feature history, not living documentation.** They record what one
feature set out to do at a point in time. For *current* system behaviour, read
[`docs/architecture.md`](../docs/architecture.md), not the specs. When a later feature reworked
or replaced an earlier one, the earlier spec is marked **Superseded** / **iterated by** so a
reader knows it no longer describes today's behaviour.

Status vocabulary:

- **Implemented** — merged; still reflects current behaviour.
- **Implemented (iterated by N)** — merged and still in the product, but a later spec reworked the
  same surface; spec N is authoritative for current behaviour.
- **Superseded by N** — the model this spec describes was replaced; it no longer reflects current
  behaviour (the capability may live on in a different form).
- **Meta** — process/housekeeping spec, not a product feature.
- **Draft (active)** — not yet implemented; the spec currently being worked on.
- **Abandoned** — early attempt that never reached a `spec.md`; superseded by a later proper spec.

## Important corrections to the historical mental model

- **Triggers and Actions are no longer first-class authored objects in the product.** The Authoring
  nav exposes only **Commands, Games, Sequences, Images** (see `src/web-ui/src/lib/navigation.ts`).
  - **Actions** as a data model were removed by **039** and replaced with **Primitive Actions**
    (tap, swipe, key, wait-for-image, connect-to-game, ensure-game-running).
  - **Triggers UI** was deleted by **020**. The trigger *evaluation engine* still exists in the
    domain (`GameBot.Domain/Triggers`, used internally for image/text detection conditions), and
    `Command.TriggerId` is a vestigial field, but there is no trigger-authoring surface.
  - Orphaned dead code still in the tree (not routed): `web-ui/src/pages/TriggersPage.tsx`,
    `services/triggers.ts`, `components/TriggerPicker.tsx`. Candidates for deletion.

## Audit table

| Spec | Title | Status |
|------|-------|--------|
| 001 | Save Configuration | Implemented |
| 002 | Config & HTTP Logging Adjustments | Implemented |
| 003 | Action/Command Refactor | Abandoned (no spec.md; superseded by 019, 039) |
| 004 | Command & Trigger Test Confidence | Implemented |
| 005 | Logging Config Refresh | Abandoned (no spec.md; superseded by 007) |
| 006 | Tesseract Logging & Coverage | Implemented |
| 007 | Runtime Logging Control | Implemented |
| 008 | Evaluate-And-Execute Trigger Guard | Implemented |
| 009 | OCR Confidence via TSV | Implemented |
| 010 | Disk-backed Reference Image Storage | Implemented |
| 011 | Image Match Detections | Implemented |
| 012 | Commands Based on Detected Image | Implemented |
| 013 | Command Sequences | Implemented |
| 014 | Sequence Logic Blocks (Loops & Conditionals) | Superseded by 031–034 |
| 015 | Web UI — Authoring (MVP) | Superseded by 018 |
| 016 | Authoring CRUD UI | Superseded by 018, 020, 039 |
| 017 | Semantic Actions UI | Superseded by 039, 064 |
| 018 | Unified Authoring Object Pages | Implemented |
| 019 | API Structure Cleanup | Implemented |
| 020 | Web UI Navigation Restructure | Implemented (removed the Triggers UI) |
| 021 | Connect to Game Action | Implemented (re-homed as a primitive action by 039) |
| 022 | Images Authoring UI | Implemented |
| 023 | Emulator Screenshot Cropping | Implemented |
| 024 | Authoring & Execution UI Visual Polish | Implemented |
| 025 | Backend and Web UI Installer | Superseded by 026 |
| 026 | Standalone Windows Installer (EXE/MSI) | Implemented |
| 027 | Installer Semantic Version Upgrade Flow | Implemented |
| 028 | Primitive Tap in Commands | Implemented (iterated by 064) |
| 029 | Persisted Execution Log | Implemented |
| 030 | Execution Logs Tab | Implemented (iterated by 063, 050) |
| 031 | Visual Conditional Sequence Logic | Superseded by 032, 033 |
| 032 | Per-Step Optional Conditions | Implemented |
| 033 | Conditional Sequence Steps (Minimal) | Implemented |
| 034 | Command Loop Structures | Implemented (iterated by 066) |
| 035 | Background Screenshot Service | Implemented |
| 036 | UI Configuration Editor | Implemented |
| 037 | Tap Wait-and-Retry Before Execution | Implemented |
| 038 | Randomized Sequence Step Delays | Implemented |
| 039 | Primitive Actions Data Model Refactor | Implemented (removed the Action data model) |
| 040 | Wait for Image Primitive Action | Implemented |
| 041 | Preserve Sequence Step Command Names | Implemented |
| 042 | Sequence Loop Step Management | Implemented (iterated by 066) |
| 043 | Reorder Spec Folders | Meta (housekeeping) |
| 044 | Drag and Drop for Command Steps | Implemented |
| 045 | Image Selector Dropdown | Implemented |
| 046 | Emulator Execution Queue | Implemented |
| 047 | Queue Templates | Implemented |
| 048 | Edit Queue Page Layout | Implemented (iterated by 061, 062) |
| 049 | Queue–Template Link with Auto-Load | Implemented |
| 050 | Execution Log Grid Cleanup | Implemented |
| 051 | Queue Execution Runtime | Implemented |
| 052 | Ensure Game Running Primitive Action | Implemented |
| 053 | Queue Sequence Scheduling | Implemented (iterated by 059, 060, 061) |
| 054 | Key Input and Swipe Primitive Actions | Implemented |
| 055 | Visual Command Recorder | Implemented |
| 056 | Simulate Recorded Step | Implemented |
| 057 | Authoring Backup & Restore | Implemented |
| 058 | Tap-Point Jitter | Implemented |
| 059 | Relative-Time Sequence Scheduling | Implemented |
| 060 | Queue-Start and After-Every-Step Scheduling | Implemented |
| 061 | Drag-and-Drop Scheduling Areas (Queue Template Editor) | Implemented |
| 062 | Queue Management Usability | Draft (active) |
| 063 | Execution Logs Reflect What Was Actually Executed | Implemented (iterated by 050) — renumbered from 049 |
| 064 | Command Editor Rework | Implemented (iterated by 054) — renumbered from 053 |
| 065 | Sequence Self-Rescheduling into the Originating Queue Run | Implemented |
| 066 | Break Step Success/Failure Execution Statuses | Implemented |
| 067 | If-Then-Else Conditions in Sequences | Implemented |
| 068 | OCR-Parsed Dynamic Offset for Reschedule-Self | Implemented |
| 069 | Go To Home Screen Action | Implemented |
| 070 | Ensure Emulator Running Action | Implemented |
| 071 | Connect-to-Game Emulator Pre-heal | Implemented |
| 072 | Live Queue Monitor View | Implemented |
| 073 | Idle-Pause the Game During Queue Gaps; Retire the MCP Server | Implemented |

## Numbering notes

- **003** and **005** are early abandoned folders that never produced a `spec.md` (only
  `plan.md`/`tasks.md`/research). Left in place as history; not part of the active sequence.
- Folders **063** and **064** were **049** and **053** — duplicate numbers created on overlapping
  dates. The earlier-created spec of each pair kept the number; the later one was moved to the next
  free sequential slot. The original merged branch names are preserved in each spec's
  **Feature Branch** line.
