# Data Model: Move the app navigation to the UI guidelines

No backend data changes. This is the UI navigation data in `src/web-ui/src/types/navigation.ts`.

## NavigationArea (changed)

| Field | Type | Note |
|-------|------|------|
| id | `NavigationAreaId` | New value `'more'` (phone only). |
| label | string | See the table below. |
| path | string | No change for the current areas. `more`: `/more`. |
| order | number | Order in its group. |
| phone | `PhoneSupportLevel` | No change for the current areas. `more`: `full`. |
| icon | `IconName` | New. |
| group | `'main' \| 'settings'` | New. |

| id | label | icon | group | phone | order |
|----|-------|------|-------|-------|-------|
| queues | Queues | queue | main | reduced | 0 |
| monitoring | Monitor | monitor | main | full | 1 |
| notifications | Alerts | alerts | main | full | 2 |
| authoring | Authoring | authoring | main | desktop-only | 3 |
| execution-logs | Execution logs | logs | settings | desktop-only | 0 |
| execution | Execution | execution | settings | hidden | 1 |
| configuration | Configuration | settings | settings | full | 2 |
| gamepacks | Gamepacks | gamepacks | settings | full | 3 |
| update | Update | update | settings | full | 4 |
| more | More | more | settings | full | 5 (not on the desktop) |

## MainTabId (new)

`'queues' | 'monitoring' | 'notifications' | 'authoring' | 'settings'`. The desktop top tabs, in this order, with the
labels Queues, Monitor, Alerts, Authoring, Settings and the icons queue, monitor, alerts, authoring, settings.

## PhoneTabId (new)

`'queues' | 'monitoring' | 'notifications' | 'more'`. The phone bottom tabs, with the labels Queues, Monitor, Alerts,
More and the icons queue, monitor, alerts, more.

## Functions (new)

| Function | Result |
|----------|--------|
| `mainTabOf(area)` | `'settings'` for a `settings` group area; else the area id. |
| `phoneTabOf(area)` | The area id for queues, monitoring, notifications; else `'more'`. |
| `hasMoreBack(area)` | True for configuration, gamepacks, update, execution-logs, authoring. |
| `desktopArea(area)` | `'execution-logs'` for `more`; else the area. |

## Constants (new)

- `SETTINGS_SUB_TABS`: execution-logs, execution, configuration, gamepacks, update.
- `SETTINGS_FIRST`: `'execution-logs'`.
- `MORE_ROWS`: configuration, gamepacks, update.
- `MORE_DESKTOP_ONLY_ROWS`: execution-logs, authoring.
- `AUTHORING_TABS`: Commands, Games, Sequences, Images, Backup & Restore (moved from `Nav.tsx` with the type `AuthoringTab`).
