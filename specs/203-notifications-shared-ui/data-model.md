# Data Model: Notifications UI

No stored data changes. These are the UI values.

## Target row (from `NotificationTargetDto`)

| UI value | Source | Rule |
|----------|--------|------|
| Name | `name` | One line, "…", tooltip. |
| Type | `type` | `telegram` → "Telegram"; other values as they are. |
| Enabled chip | `enabled` | `true` → state `done`, text "On"; `false` → state `stopped`, text "Off". |
| Chat ID | `settings.chatId` | Monospace. Empty → "—". |
| Bot token | `hasSecret`, `secretHint` | `hasSecret` and a hint → "Saved (<hint>)"; `hasSecret` and no hint → "Saved"; else "Not set". |
| Phone meta | `type`, `settings.chatId` | "Telegram · <chat ID>"; no chat ID → "Telegram". |

## Target row actions

| Layout | Visible | Menu |
|--------|---------|------|
| Desktop | Send test message (icon `send`), Edit (icon `edit`) | Delete (icon `delete`, danger) |
| Phone | Send test message (icon `send`) | Edit (icon `edit`), Delete (icon `delete`, danger) |

Accessible names: "Send test message <name>", "Edit <name>", "Delete <name>", "More actions for <name>". The test
action is off while the test call of the same target runs.

## Target form values

| Field | Rule |
|-------|------|
| Name | Required. Trimmed. Max. 100 characters. Error "Enter a name.". |
| Bot token | Optional. Password field. Empty → no token in the call (the stored token stays). |
| Chat ID | Required. Trimmed. Error "Enter a chat ID.". |
| Enabled | `Toggle`. Default on for a new target. |

## Form states

`idle` → `busy` (save, or save and test) → `idle` with a message or an API error. While `busy`, all footer buttons
are off.

## Queue level row

| UI value | Source |
|----------|--------|
| Queue | `QueueDto.name` |
| Level | `notificationLevel` (`none` = "None", `failure` = "Failure", `successAndFailure` = "Success + Failure"); missing → `none` |
