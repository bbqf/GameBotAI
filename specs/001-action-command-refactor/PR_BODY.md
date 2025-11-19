# Spec: Action/Command Refactor (001)

## Summary
Refactors the domain to:
- Rename Profile to Action
- Decouple Triggers from Actions (no implicit binding)
- Remove automated/background trigger evaluation entirely
- Introduce Command as an executable composite of Actions and/or Commands
- Allow optional Trigger association per Command (evaluate-and-execute)
- Provide force-execute to run a Command ignoring Trigger

Spec: specs/001-action-command-refactor/spec.md  
Checklist: specs/001-action-command-refactor/checklists/requirements.md

## What’s Included
- New specification document defining goals, scope, requirements, entities, and acceptance tests
- Quality checklist to validate spec readiness
- No implementation code changes in this PR

## Clarification Resolution
Decision: A — Breaking rename now. External API, contracts, and persistence naming migrate from "Profile" to "Action" as part of this feature (no deprecation window). Migration notes will include updated endpoints, payloads, and client guidance.

## Success Criteria
- CRUD for Action, Trigger, Command defined in spec
- Command composition validated as acyclic
- Evaluate-and-execute runs only when Trigger = Satisfied; force-execute bypasses Trigger
- No background trigger evaluation present post-change

## Next Steps
- On approval and clarification selection, proceed with planning (`/speckit.plan`) and implementation PRs.
