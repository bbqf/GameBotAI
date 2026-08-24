# Quickstart & Manual Conversion Guide: Sequence & Command Parameters

**Feature**: 078-sequence-parameters
**Audience**: the operator, working entirely in the web UI
**Satisfies**: FR-033, FR-034 — there is **no automatic migration**. This document *is* the migration
path. Nothing you already have changes until you change it.

---

## Part 1 — The 60-second version

If your emulator instances differ only by their ADB serial, you do not need to declare anything.
A queue already knows its own serial, and every sequence and command it runs can now read it as
`{{queue.emulatorSerial}}`.

**Read this first:** every queue that will run the shared command must have its **Emulator serial**
field set correctly, because that field is now the value being propagated. Check all of them before
you start.

---

## Part 2 — Worked conversion: three "ensure game running" commands into one

**Starting point** (the situation this feature exists to remove):

| Command | Sequence | Queue | ADB serial |
|---|---|---|---|
| `Ensure Game 5558` | `Daily 5558` | `PNS Daily 5558` | `emulator-5558` |
| `Ensure Game 5560` | `Daily 5560` | `PNS Daily 5560` | `emulator-5560` |
| `Ensure Game 5562` | `Daily 5562` | `PNS Daily 5562` | `emulator-5562` |

**Target**: one `Ensure Game Running` command, one `Daily` sequence, three queues.

### Step 1 — Verify each queue's emulator serial

Queues → open each queue → confirm **Emulator serial** matches the instance it drives. This is the
value the whole conversion rides on. If a queue also sets **Emulator instance name** or **index**,
note them — they are available as `{{queue.instanceName}}` and `{{queue.instanceIndex}}`.

### Step 2 — Pick one command to become the shared one

Choose the copy you trust most — the one whose logic is current. Commands → open it → **Duplicate**,
and name the copy `Ensure Game Running`. Working on a copy means the three originals stay untouched
and runnable until you have proved the new one.

### Step 3 — Replace the hard-coded serial with the queue built-in

In the copy, find every field holding a literal serial. On an ensure-emulator-running step that is
**ADB serial**; other step types may have their own.

For each one: click the **{ }** button at the right of the field, and choose
**`queue.emulatorSerial`** from the list. The field becomes `{{queue.emulatorSerial}}`.

> Use the picker rather than typing the braces. It only offers names that are actually in scope, so
> it cannot produce a name that fails to resolve. (The picker is available on the ensure-emulator
> ADB-serial field; elsewhere, type the reference exactly — see Part 3a.)

Save. If the save is rejected, the message names the field and the problem — fix it and save again.

### Step 4 — Do the same for the sequence

Sequences → duplicate `Daily 5558` → rename to `Daily`. Open its command steps and repoint each one
that referenced `Ensure Game 5558` at the new shared `Ensure Game Running`. Save.

**Nothing else to do here.** A value the sequence does not bind is inherited from the enclosing scope
automatically, so the queue's serial reaches the command with no configuration on the sequence at
all. That is the whole point of the inheritance rule — a sequence never has to re-declare a value
merely to pass it through.

> Explicitly overriding a parameter *at a particular sequence step* (rather than per queue or per
> template entry) is not yet editable in the sequence editor. It is rarely what you want — a value
> that differs per instance belongs on the queue or the template entry, which is what the rest of
> this guide uses.

### Step 5 — Point one template at the shared sequence, and test it

Queue templates → open the template used by `PNS Daily 5558` → change the entry from `Daily 5558` to
`Daily`. Save.

Start `PNS Daily 5558`. Then **verify before going further** (Step 7).

### Step 6 — Roll out to the other two

Repeat Step 5 for the 5560 and 5562 templates. No new parameter values are needed — each queue
supplies its own serial.

### Step 7 — Verify, on every instance, before deleting anything

Execution logs → open the run → open the **command** entry, and look at its detail lines. Confirm:

- a line reading `Step 0 resolved 1 parameter(s): queue.emulatorSerial=emulator-5558` — the value must
  match *that* queue's serial. (Each such line also records which scope supplied the value: `queue`
  for a built-in, `entry` for a template-entry value, `default` for a declared default.)
- the step outcome is `executed`, not `skipped_parameter_unresolved` or `skipped_invalid_config`;
- the action landed on the right instance — the emulator you expected actually reacted.

> If a parameter could not be resolved you will instead see
> `Step 0 was not executed: Step '0': parameter 'x' used by field '…' could not be resolved from any
> scope.` — nothing was sent to the device, so this is safe to fix and re-run.

Repeat for 5560 and 5562. **All three must pass.** A wrong serial here means a queue is driving
another instance, which is exactly the failure this conversion is meant to prevent.

### Step 8 — Delete the redundant copies, safely

For each old command and sequence, before deleting:

1. Sequences → search for the command by name. If any sequence still lists it as a step, that
   sequence has not been converted — go back to Step 4.
2. Queue templates → search for the sequence by name. If any entry still references it, that
   template has not been converted — go back to Step 5.
3. Only when both searches come back empty, delete it.

Delete one, then run the affected queue once more before deleting the next. If something was still
referencing it, the run fails loudly with `Command '<id>' was not found` rather than silently doing
nothing — but it is far easier to recover if you deleted one thing rather than six.

---

## Part 3 — When the queue does not already know the value

Use a declared parameter when the differing value is not the serial, instance name, instance index,
or linked game.

### 3a. Declare it on the command that consumes it

Commands → open → **Parameters** → **Add**:

| Field | Example | Notes |
|---|---|---|
| Name | `waitMs` | letters, digits, underscore; cannot be `iteration` or start with `queue.` |
| Type | Number | numeric fields accept only a whole-field placeholder |
| Default | `5000` | used when nobody supplies a value — makes the parameter safe to leave alone |
| Required | off | on = the queue refuses to start until every entry can supply it |
| Description | `Poll interval before giving up.` | shown in the picker; write it for your future self |

Then put the reference into the field and save.

- On the **ADB serial** field of an ensure-emulator-running step, use the **{ }** picker — it lists
  every name in scope with its description and inserts a valid reference, so you never type braces.
- Other fields are plain inputs for now: type the reference yourself, exactly `{{waitMs}}`. Spelling
  matters — names are case-sensitive, and a name nothing can supply is rejected when you save, naming
  the field and the parameter.

### 3b. Supply the value where it differs

Queue templates → open the template → click **Parameters** on the entry → clear **Inherit** on the
row and type the value. Use this whenever the value is a property of *that instance*.

The row shows the effective value and where it came from — `set on this entry`, `from the queue`, or
`declared default` — so you can confirm the result without running anything. An entry that supplies
any value is badged **Parameters** in the template list, so overridden entries are visible at a
glance.

### 3c. Passing a value straight through to a nested command

The intermediate sequence does **not** need to declare anything. On the template entry, use **Add
value** to supply the name directly — it reaches any command underneath that declares it, at any
depth.

If you mistype the name, the entry saves with a warning: *"value 'adbSeril' is not used by anything
in this entry"*. That warning is the typo detector — do not ignore it.

---

## Part 4 — Reference

### Built-in names (always available inside a queue run, never declarable)

| Name | Comes from | Type |
|---|---|---|
| `queue.emulatorSerial` | the queue's Emulator serial | text |
| `queue.instanceName` | the queue's Emulator instance name | text |
| `queue.instanceIndex` | the queue's Emulator instance index | number |
| `queue.gameId` | the queue's linked game | text |

A built-in whose queue field is blank is **not in scope** — referencing it fails exactly like an
unknown name, rather than resolving to nothing.

### Which value wins

1. A value set on the call site (template entry, or the sequence's command step)
2. A value inherited from further out (the queue built-ins, or an outer entry value)
3. The parameter's declared default
4. Otherwise the step fails — nothing is dispatched to the device

### Reading the failure messages

| Message | Meaning | Fix |
|---|---|---|
| `parameter 'x' … could not be resolved from any scope` | nothing supplied it and it has no default | supply it on the entry, or give the declaration a default |
| `parameter 'x' resolved to 'abc', which is not a whole number` | a text value reached a numeric field | fix the value, or change the declared type |
| `value 'y' is not used by anything in this entry` (warning) | ad-hoc name matches no declaration below | check the spelling against the command's Parameters panel |
| queue start refused, `missing_required_parameters` | a required parameter has no value and no default | set it on the named entry, or clear the Required flag |

### Things that deliberately cannot be parametrized

- Which command a step runs, and which sequence a template entry runs. These stay literal so the
  "this reference is dangling" check keeps working.
- `{{iteration}}` outside a loop — it has no meaning there and is rejected at save time.

### Rolling back

Nothing was migrated automatically, so rolling back is just editing: put the literal value back in
the field, or repoint the template entry at the old sequence. As long as you have not completed
Step 8, the originals are still there.
