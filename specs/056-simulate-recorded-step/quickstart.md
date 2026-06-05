# Quickstart: Simulate Recorded Step

## Overview

The command recorder's step list now has a **Run** button on each step and a **Run all** button on the toolbar. These execute the step(s) directly against the connected emulator without leaving the recorder, closing the record-verify-adjust loop.

## How to use

### Run a single step

1. Open the command editor and click **Record steps** to open the VisualStepPicker.
2. Record one or more steps by tapping/swiping on the screenshot or pressing keys.
3. Click **▶ Run** next to any step in the list.
4. A spinner appears on the step while it executes.
5. On completion, the step shows a **✓** (success) or **✗ <reason>** (failure / timeout).
6. Edit the step if needed (status badge resets automatically), then re-run to verify.

### Run all steps

1. With two or more steps recorded, click **Run all** in the toolbar.
2. Steps execute in order; the currently-running step is highlighted.
3. Execution stops at the first failure; that step is highlighted in red.
4. Adjust the failing step and click **Run all** again.

### Confirm and add steps

Once satisfied, click **Confirm** to add all recorded steps to the command. The execution status badges are stripped — only the step actions are added.

## Behaviour notes

| Situation | Behaviour |
|---|---|
| Emulator not running | Inline error on the step; recorder stays open |
| Step hangs | Automatically fails with "timeout" after 10 seconds |
| Edit a step after running | Status badge resets to idle; re-run to re-verify |
| Double-click Run | Second click ignored while step is executing |
| Edit/reorder during Run all | Editing locked until Run all completes or stops |

## Keyboard

No keyboard shortcuts are added for Run. Tab and Enter can be used to focus and activate the Run button in the step row.
