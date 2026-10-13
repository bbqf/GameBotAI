# Data Model: Mobile-Friendly UI

**Feature**: 139-mobile-friendly-ui | **Date**: 2026-10-13

This feature adds no stored data and no API field. The items below are front-end types and rules.

## LayoutMode

| Value | Rule |
|-------|------|
| `phone` | Screen width is 768 CSS px or less, OR screen height is 500 CSS px or less. |
| `desktop` | All other screens. |

The value changes when the window size or the phone orientation changes. No reload.

## PhoneSupportLevel

| Value | Meaning on a phone |
|-------|--------------------|
| `full` | The area has the same functions as on the desktop, in a phone layout. |
| `reduced` | The area has a subset of functions. Other functions show the "Use a larger screen" notice. |
| `desktop-only` | The area shows the `DesktopOnlyNotice` and a way back. |

## Area (existing type `NavigationArea`, one new field)

| Field | Type | Note |
|-------|------|------|
| `id` | `NavigationAreaId` | Existing. |
| `label` | string | Existing. |
| `path` | string | Existing. |
| `order` | number | Existing. |
| `phone` | `PhoneSupportLevel` | New. Required. A new area cannot compile without it. |

Assigned values:

| Area | Level | Reason |
|------|-------|--------|
| Queues | `reduced` | Status, start, stop, entry enable switch. No create, edit form, schedule, reorder, duplicate, or delete. |
| Execution | `full` | Sessions, run details, and quick run reflow as cards. |
| Notifications | `full` | Settings reflow. The level table becomes a list of rows. |
| Configuration | `full` | Token and connection fields. Parameter editor reflows. |
| Update | `full` | Short page. |
| Authoring | `desktop-only` | Per clarification (all Authoring pages). |
| Execution Logs | `desktop-only` | Per spec FR-009. |

## PhoneTab (derived, not stored)

The tab bar shows `Queues`, `Execution`, `Notifications`, and `More`. The `More` sheet lists Configuration, Update, and each `desktop-only` area with a "Desktop only" label.

## ConnectionState

| Value | Rule |
|-------|------|
| `online` | The browser reports online AND the last `/health` call succeeded. |
| `lost` | The browser reports offline OR the last `/health` call failed. |

State transition: `online` to `lost` shows the banner and disables control actions. `lost` to `online` hides the banner and reloads the visible data. The UI never repeats an action during the change.

## QueueEntrySwitch (view of an existing template entry)

| Field | Source |
|-------|--------|
| `sequenceName` | `QueueTemplateEntryDto.sequenceName` |
| `enabled` | `QueueTemplateEntryDto.enabled` |
| `scheduleType`, `timerTimeOfDay`, `timerRelativeOffset`, `parameterValues` | Read from the template and written back unchanged. |

Validation: the save sends every entry of the template, in the same order, with only one `enabled` value changed. The save uses `overwrite: true` and the existing template name.
