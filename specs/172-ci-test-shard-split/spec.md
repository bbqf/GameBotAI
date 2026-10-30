# Feature Specification: Split the Largest CI Test Shard

**Feature Branch**: `172-ci-test-shard-split`  
**Created**: 2026-10-30  
**Status**: Implemented  
**Input**: User description: "The CI build times have grown enormously again, the test4 bucket has got 4x larger than the rest of them. Break that down into even more parallel runners and establish a rule that has to be followed whenever the test bucket run time (local) increases above 20% above the average across all testers, it has to be split again"

## Clarifications

### Session 2026-10-30

- Q: How is the rule enforced? → A: Written rule plus a local check script. The contributor must run it, and the pull request checklist asks for the result. CI does not run it.
- Q: Which test projects does the rule cover? → A: All test projects (unit, contract, integration). Each shard is compared with the average of its own project.
- Q: Must this change also rebalance the unit and integration shards? → A: No. Only the contract-4 shard is split now. The check script lists any other shard above the limit, and those are fixed in later changes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shorter CI runs (Priority: P1)

A developer opens a pull request and waits for CI. One test shard (the fourth contract shard, "contract-4") now runs about 4 times longer than the other shards. All other shards must wait for it, so the total CI time is set by this one shard. The developer wants the long shard split into more parallel shards, so that CI ends sooner.

**Why this priority**: The longest shard sets the CI time for every pull request. This is the direct cause of the problem.

**Independent Test**: Run the full set of shards on a local machine and in CI. Compare the run time of the longest shard before and after the split. Confirm that no test is lost or run twice.

**Acceptance Scenarios**:

1. **Given** the shard that is 4 times larger than the others, **When** the shard list is changed, **Then** that shard is replaced by two or more smaller shards that run in parallel.
2. **Given** the new shard list, **When** all shards run, **Then** every test that ran before the change runs exactly once.
3. **Given** the new shard list, **When** the run time of every contract shard is measured locally, **Then** no contract shard is more than 20% above the average of all contract shards.
4. **Given** a new test class is added later, **When** CI runs, **Then** the test class runs in one shard and is never lost.

---

### User Story 2 - A rule that keeps shards balanced (Priority: P1)

A maintainer wants a written rule, so that shard growth does not slow CI again without notice. The rule says: when the local run time of a shard is more than 20% above the average local run time of all shards, the shard MUST be split again.

**Why this priority**: Without the rule, the same problem comes back. The user asked for the rule explicitly.

**Independent Test**: Read the project rules and find the 20% limit, the way to measure, and the required action. Then apply the rule to the current shard times and confirm that it gives the correct result.

**Acceptance Scenarios**:

1. **Given** the project rules, **When** a contributor reads them, **Then** they find the 20% limit, the way to measure the shard run times locally, and the action to take when the limit is exceeded.
2. **Given** a shard whose local run time is more than 20% above the average of all shards, **When** a contributor prepares a change, **Then** the rule requires them to split that shard in the same change or in a follow-up change before it merges.
3. **Given** all shards are within 20% of the average, **When** a contributor prepares a change, **Then** the rule requires no split.

---

### User Story 3 - A check that shows the shard times (Priority: P2)

A contributor wants an easy way to measure the local run time of each shard and to see which shard breaks the rule.

**Why this priority**: The rule is only useful if people can apply it with little effort.

**Independent Test**: Run the check on a local machine. Confirm that it prints the run time of each shard, the average, the 20% limit, and which shards are above the limit.

**Acceptance Scenarios**:

1. **Given** a local checkout, **When** the contributor runs the check, **Then** it prints the run time of each shard, the average, and the limit.
2. **Given** a shard above the limit, **When** the check ends, **Then** it names the shard and reports a failure.
3. **Given** all shards within the limit, **When** the check ends, **Then** it reports success.

---

### Edge Cases

- A shard that collects coverage or runs the performance gate must keep this duty after the split. Only one shard may hold each duty.
- Tests that are pinned to a shard (for example, tests that cover the coverage gates) must stay in the shard that holds the duty.
- A test project with one shard has no average to compare. The rule does not apply to it.
- A split that makes one new shard much smaller than the average is allowed. The rule limits only the upper side.
- The local run time changes between runs. The check must use a method that gives a stable result (for example, the same machine and a repeat run before it reports a failure).
- The shard names and number of shards change. Anything that uses the shard names (for example, required status checks) must stay valid.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The fourth contract test shard MUST be split into at least two shards that run in parallel in CI.
- **FR-002**: After the split, every test that ran before MUST run in exactly one shard.
- **FR-003**: After the split, the local run time of each contract shard MUST be at most 20% above the average local run time of all contract shards.
- **FR-003a**: This change MUST NOT rebalance the unit or integration shards. If the shard check finds a unit or integration shard above the limit, it MUST list the shard as a finding for a later change.
- **FR-004**: A shard that holds a special duty (coverage collection, performance gate) MUST keep that duty, and only one shard MUST hold each duty.
- **FR-005**: A new test class that is not named in any shard MUST still run in exactly one shard.
- **FR-006**: The project rules MUST state that a shard whose local run time is more than 20% above the average of all shards of its own test project (unit, contract, or integration) MUST be split again.
- **FR-007**: The rule MUST state how to measure the local run time of a shard, and when to measure it (for example, when a contributor adds many tests or sees slow CI).
- **FR-008**: The project MUST give contributors a way to measure the local run time of every shard and to report the shards that are above the limit.
- **FR-009**: The shard check MUST report a failure (non-zero exit status) and name each shard above the limit.
- **FR-009a**: The shard check MUST run on a developer machine only. CI MUST NOT run it, because local run times differ between machines.
- **FR-009b**: The pull request checklist MUST ask the contributor to run the shard check and give the result when the change adds many tests or changes the shard list.
- **FR-010**: Required status checks and other places that use shard names MUST work after the change.
- **FR-011**: The total CI run time MUST NOT increase because of the extra runners (for example, from repeated build or restore steps that the split adds).

### Key Entities

- **Test shard**: A named group of tests that runs on one CI runner. It has a list of tests, a local run time, and optional duties (coverage, performance gate).
- **Shard list**: The single source of truth that names all shards of each test project and the tests in each shard.
- **Shard balance rule**: The 20% limit above the average local run time, and the action it requires (split the shard).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the change, the longest contract shard runs for at most 1.2 times the average contract shard run time, measured locally.
- **SC-002**: The CI run time of a pull request is shorter than before the change. The time saved is at least half of the difference between the old longest shard and the new longest shard.
- **SC-003**: 100% of the tests that ran before the change run exactly once after the change (the test count before and after is the same).
- **SC-004**: A contributor can measure all shard run times and find the shards that break the rule in one command, without manual calculation.
- **SC-005**: The rule is written in one place that every contributor reads before they change the shard list.

## Assumptions

- "test4 bucket" means the fourth contract test shard ("contract-4"), which is the largest shard and is the complement of all named filters. If the largest shard is another one, the same rule and split apply to that shard.
- "Local run time" means the time a shard needs on a developer machine, not on a CI runner.
- The 20% limit compares each shard with the average of all shards in the same test project (unit, contract, integration). The rule covers all three projects, but only the contract split is in scope now.
- The shard list stays in its current single file, and the existing complement shard stays the place where new test classes go.
- The number of new shards is the smallest number that meets FR-003. More shards are allowed if needed.
- The rule is enforced by the contributor process (rule text, local check script, pull request checklist), not by a CI gate.
- The rule is added to the project constitution or to the contributor rules, the place that the plan phase selects.
