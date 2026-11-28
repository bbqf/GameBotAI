# Feature Specification: Move the app navigation to the UI guidelines

**Feature Branch**: `205-navigation-shared-ui`  
**Created**: 2026-11-28  
**Status**: Draft  
**Input**: User description: "Move the app navigation to docs/ui-guidelines.md section 3 (rules N1-N7) and to the mockups." The full description is in the pipeline call. The user approved the navigation of the mockups (`docs/ui-guidelines/1-list-pattern.png`, `docs/ui-guidelines/3-settings-images-backup-panel.png` sections E/F) and, on 2026-11-28, the Execution area as a sub-tab of Settings.

## Background

The web UI has these areas now: Queues, Monitoring, Authoring, Execution, Execution Logs, Configuration, Update, Gamepacks, and Notifications.

- **Desktop**: a bar with the text "GameBot Web UI", then one row of 9 text tabs with no icons. Authoring has its own `<h1>Authoring</h1>` and a second tab row (Commands, Games, Sequences, Images, Backup & Restore). Some pages show an `<h1>` title, some show none. The desktop does not show the connection state.
- **Phone**: a bottom bar with 3 tabs (Queues, Monitoring, More). The icons are text glyphs. More opens a sheet with a list of the other areas, with "Desktop only" notes.

Rules N1 to N7 of the guideline give one navigation for both screens: the same 5 main areas with the same names, icons, and order (Queues, Monitor, Alerts, Authoring, Settings / More); top tabs with icons on the desktop and a second row of sub-tabs; a bottom bar with 4 tabs and a More page with menu rows on the phone.

## Clarifications

### Session 2026-11-28

- Q: Where does the Execution area go? → A: It is a sub-tab of Settings on the desktop: Execution logs, Execution, Configuration, Gamepacks, Update. The phone does not show it (as now). Rationale: the user chose this option on 2026-11-28.

The other answers are from the pipeline with no manual review. Each is the most reasonable option, chosen from the guideline, the mockups, and the code.

- Q: Which area opens on the desktop when the address names no area? → A: Authoring > Commands, as now. Rationale: the current default; a change of the first page is not part of the guideline.
- Q: What does the back arrow on a page opened from More do in the phone history? → A: It opens the More page with one new history entry (`pushState`), as a tap on the More tab does. It does not call the browser back. Rationale: the page can be the first entry (an address opened directly); a browser back would leave the web UI. The same rule as a tab tap keeps one simple history model.
- Q: Does the phone app bar show the back arrow on the More page itself? → A: No. Only the pages opened from More (Configuration, Gamepacks, Update, and the desktop-only notice of Execution logs or Authoring) show it. Rationale: More is a main tab, and main tabs have no back arrow (rule N6 is for detail pages).
- Q: What does "Use a computer" mean for the desktop-only notice that an address opens on the phone? → A: The notice stays (title = area name, the text "Use a larger screen for this area.") and its button "Back to More" opens the More page. Rationale: the More page lists these areas; Queues is not their parent.
- Q: Does the More page need its own page header when the phone app bar already shows "More"? → A: Yes, the page uses `PageHeader` "More", which the phone hides for the eye (feature 204, a top-level header with no actions). Screen readers read it. Rationale: the same rule as the other top-level pages; no second title shows.
- Q: Does the document title "GameBot Web UI" change? → A: Yes, to "GameBot". Rationale: one name in the app bar and the browser tab; the old name goes away with the old bar (analyze finding A1).
- Q: What does the desktop connection state show while the first check runs? → A: "Online" when the browser is online (the current start value of the connection check). Rationale: the same start value as the phone; no third state.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Use the same main areas on the desktop (Priority: P1)

The operator opens the web UI on a computer. The app bar shows "GameBot", the 5 top tabs Queues, Monitor, Alerts, Authoring, and Settings, each with an icon, and the connection state on the right.

**Why this priority**: The app bar is on each page. It is the first part of the guideline that the operator sees.

**Independent Test**: Open the web UI at 1280 px. Read the app bar. Click each top tab.

**Acceptance Scenarios**:

1. **Given** the desktop, **When** the web UI opens, **Then** the app bar is 56 px high and shows "GameBot", the 5 top tabs in this order with an icon and a name, and "Online".
2. **Given** the service does not answer the health call, **When** the next check runs, **Then** the app bar shows "Connection lost" in the danger color.
3. **Given** the Alerts top tab, **When** the operator clicks it, **Then** the Alerts page (the current Notifications page, with the title "Alerts") shows, and the tab has the primary color and a 3 px bottom line.
4. **Given** the address `?area=monitoring` or `?area=notifications`, **When** the web UI opens, **Then** Monitor or Alerts shows and its top tab is active.

---

### User Story 2 - Use the sub-tabs of Authoring and Settings on the desktop (Priority: P1)

The operator clicks Settings. A second row under the app bar shows Execution logs, Execution, Configuration, Gamepacks, and Update. Execution logs opens. Authoring shows its sub-tabs in the same row style.

**Why this priority**: The 5 areas under Settings are only reachable through the sub-tabs.

**Independent Test**: Click Settings and each sub-tab. Click Authoring and each sub-tab. Open the old addresses of the Settings areas.

**Acceptance Scenarios**:

1. **Given** the desktop, **When** the operator clicks Settings, **Then** the sub-tab row (44 px) shows the 5 Settings sub-tabs in order, Execution logs is active, and its page shows with the title "Execution logs".
2. **Given** a Settings sub-tab, **When** the operator clicks Configuration, **Then** the Configuration page shows with the title "Configuration", and the Settings top tab stays active.
3. **Given** the address `?area=update` (or `configuration`, `gamepacks`, `execution`, `execution-logs`, or the paths of these areas), **When** the web UI opens, **Then** that page shows, the Settings top tab is active, and the matching sub-tab is active.
4. **Given** the address `?area=more` on the desktop, **When** the web UI opens, **Then** Settings > Execution logs shows.
5. **Given** Authoring, **When** it shows, **Then** the sub-tab row shows Commands, Games, Sequences, Images, and Backup & Restore, and no `<h1>Authoring</h1>` shows.
6. **Given** an active sub-tab (Authoring or Settings), **When** the operator clicks it again, **Then** the page gets the same reset signal as now (it goes back to its list, with the current unsaved-changes check).

---

### User Story 3 - Use the bottom tab bar and the More page on the phone (Priority: P1)

The operator opens the web UI on a phone. The bottom bar shows Queues, Monitor, Alerts, and More with icons. More opens a page with menu rows for Configuration, Gamepacks, and Update, and dimmed rows for the areas that need a computer.

**Why this priority**: Alerts becomes a main tab on the phone, and the More page replaces the sheet.

**Independent Test**: Open the web UI at 360 x 640. Tap each tab. Tap More, then Configuration, then the back arrow.

**Acceptance Scenarios**:

1. **Given** the phone, **When** the web UI opens, **Then** the bottom bar (64 px) shows Queues, Monitor, Alerts, and More in this order, each with an icon and a name, and each tab is at least 44 px high.
2. **Given** the phone, **When** the operator taps More, **Then** the More page shows with the header "More", the rows Configuration, Gamepacks, and Update (56 px, icon, name, chevron), the label "Desktop only", and the dimmed rows Execution logs and Authoring with the text "Use a computer". The More tab is active.
3. **Given** the More page, **When** the operator taps Configuration, **Then** the Configuration page shows, the app bar shows "Configuration" and a back arrow, and the More tab stays active.
4. **Given** a page opened from More, **When** the operator taps the back arrow, **Then** the More page shows.
5. **Given** the More page, **When** the operator taps a dimmed row, **Then** nothing happens.
6. **Given** the address `?area=authoring` on the phone, **When** the web UI opens, **Then** the desktop-only notice shows with a button that opens the More page.
7. **Given** the phone, **When** the operator uses the browser back control after Queues > More > Configuration, **Then** the More page shows, then Queues (one history entry for each step).

### Edge Cases

- A text input has the focus on the phone: the bottom bar hides (as now).
- The queue entry page has unsaved edits: a tap on a tab or on the back control asks the operator first (as now).
- The phone at 320 px: the 4 tabs and the More rows fit; no sideways scroll.
- The phone in landscape (640 x 360): the phone layout; the bottom bar shows.
- The desktop window at 1024 px: the 5 top tabs and the connection state fit in one row of 56 px.
- The address names the hidden area Execution on the phone: Queues shows (as now).
- The address `?area=more` on the desktop: Settings > Execution logs shows.
- The connection is lost on the desktop and comes back: the reload signal fires once (as on the phone now).

## Requirements *(mandatory)*

### Functional Requirements

**Main areas**

- **FR-001**: Both layouts MUST show the main areas with the same names, icons, and order: Queues (icon `queue`), Monitor (`monitor`), Alerts (`alerts`), Authoring (`authoring`, desktop only), and Settings (`settings`, desktop) / More (`more`, phone).
- **FR-002**: The labels MUST change: "Monitoring" becomes "Monitor" and "Notifications" becomes "Alerts" in the navigation, in the phone app bar title, and in the page header title of these pages. The area ids `monitoring` and `notifications`, the address values, and the paths `/monitoring` and `/notifications` MUST NOT change.
- **FR-003**: The shared icon set MUST get the icons `queue`, `monitor`, `alerts`, `authoring`, `logs`, `execution`, `gamepacks`, and `update`, in the same Material style as the current icons.

**Desktop app bar and sub-tabs**

- **FR-004**: The desktop app bar MUST be one row of 56 px: "GameBot" on the left, then the 5 top tabs (icon 20 px and name; the active tab has the primary color and a 3 px bottom line), then the connection state on the right.
- **FR-005**: The connection state MUST show a green dot and "Online", or a red dot and "Connection lost". The desktop MUST check the connection in the same way as the phone (health call every 10 s while the page is visible), and the reload signal MUST fire when the connection comes back.
- **FR-006**: The old "GameBot Web UI" bar and the old row of 9 text tabs MUST go away.
- **FR-006a**: The document title (the browser tab text) MUST be "GameBot", the same name as the app bar brand.
- **FR-006b**: At a desktop width of 1024 px, the app bar MUST stay one row of 56 px (the tabs and the connection state do not wrap).
- **FR-007**: One shared sub-tab component MUST show the second row: 44 px high, 13 px text, a tab list with the selected state, keyboard focus, and hover styles only for devices that can hover.
- **FR-008**: Authoring MUST show the sub-tabs Commands, Games, Sequences, Images, and Backup & Restore in the shared sub-tab row. The `<h1>Authoring</h1>` MUST go away. A click on the active sub-tab MUST send the reset signal (as now).
- **FR-009**: Settings MUST show the sub-tabs Execution logs, Execution, Configuration, Gamepacks, and Update, which open the areas `execution-logs`, `execution`, `configuration`, `gamepacks`, and `update`. These areas keep their ids and addresses.
- **FR-010**: The Settings top tab MUST be active when one of these 5 areas is active. A click on the Settings top tab MUST open Execution logs. A click on the active Settings sub-tab MUST do the same as a click on the active area now.
- **FR-011**: On the desktop, the address `?area=more` MUST open Settings > Execution logs.
- **FR-012**: The 5 pages under Settings MUST show a page header with the sub-tab name as the title: "Execution logs", "Execution", "Configuration", "Gamepacks", "Update". This replaces the current `<h1>` titles. The page content MUST NOT change otherwise.

**Phone tab bar and More page**

- **FR-013**: The phone bottom bar MUST be 64 px high and show 4 tabs: Queues, Monitor, Alerts, and More, each with a shared icon and a name. Text glyphs MUST NOT be used as icons. The bar MUST hide while a text input has the focus, and the leave guard MUST apply to each tab (as now).
- **FR-014**: The More tab MUST open the More page (phone-only area `more`, address `area=more`, one history entry as for other area changes).
- **FR-015**: The More page MUST show a page header "More" and menu rows of 56 px (icon 22 px in the muted color, name 15 px, chevron on the right; the full row is the tap target) in this order: Configuration, Gamepacks, Update. Under them, the label "Desktop only" and the rows Execution logs and Authoring, dimmed (opacity about 0.55), with no chevron, with the status line "Use a computer", and not active (they do nothing when tapped and are marked as disabled for screen readers).
- **FR-016**: The Execution area MUST stay hidden on the phone. A phone address for a hidden area MUST show Queues (as now).
- **FR-017**: The old More sheet MUST go away.
- **FR-018**: The More tab MUST be active on the More page and on each page opened from it (Configuration, Gamepacks, Update, and a desktop-only notice).
- **FR-019**: A page opened from More (Configuration, Gamepacks, Update, and the desktop-only notice) MUST show a back arrow in the phone app bar. The back arrow MUST open the More page with one new history entry, as a tap on the More tab does. The More page and the other main tabs MUST NOT show a back arrow.
- **FR-020**: A desktop-only area requested on the phone MUST show the desktop-only notice (the area name and "Use a larger screen for this area."). Its button MUST be the shared secondary button "Back to More" and MUST open the More page.

**Behavior that stays**

- **FR-021**: All current behavior MUST stay: the phone history (first entry, back and forward, the leave guard of the queue entry page), the phone redirect of hidden areas to Queues, the address sync (tab, area, queue, entry), the reset signals, the phone connection banner, the phone layout breakpoint, and the first area when the address names none (desktop: Authoring > Commands; phone: Queues).
- **FR-021a**: The More page MUST use the shared page header "More". On the phone the header title is hidden for the eye only (feature 204), so no second title shows.
- **FR-022**: On the phone, each tap target MUST be at least 44 px, and no sideways scroll MUST occur at 320, 360, and 390 px.

**Documents and tests**

- **FR-023**: `docs/ui-guidelines.md` section 3 MUST show the Settings sub-tabs with Execution (N4) and the exact More rows (N5). Section 13 MUST list the sub-tab component and the new icons.
- **FR-024**: Unit tests and browser tests that use the old labels or parts ("Monitoring", "Notifications", the More sheet, "GameBot Web UI", the Authoring `<h1>`, the old tab row) MUST change to the new labels and parts. No behavior check may be removed.
- **FR-025**: New unit tests MUST cover the sub-tab component, the desktop app bar, the Settings group logic, the phone tab bar, and the More page.
- **FR-026**: Browser tests MUST check, on the desktop at 1280 px: the 56 px app bar with the 5 top tabs in order with icons, the connection state, Settings opens Execution logs with 5 sub-tabs, the 44 px sub-tab row, the Authoring sub-tabs, and the old area addresses open the correct page and tab. On the phone at 360 x 640: the 4 bottom tabs in order and at least 44 px high, the 56 px More rows, the dimmed rows that do nothing, a row that opens its page and the back arrow that returns to More, and no sideways scroll at 320, 360, and 390 px.

### Key Entities

- **Navigation area**: id, label, address path, order, phone support level. New: icon, and the group (main area, or a sub-area of Authoring or Settings). New phone-only area `more`.
- **Navigation group**: Settings (desktop) / More (phone). It holds the areas Execution logs, Execution, Configuration, Gamepacks, Update.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The desktop shows 5 top tabs and the phone shows 4 bottom tabs; the names and icons of Queues, Monitor, and Alerts are the same on both (100%).
- **SC-002**: The operator reaches each of the 9 current areas from any page with at most 2 clicks on the desktop and, for the areas that the phone shows, at most 2 taps on the phone.
- **SC-003**: 100% of the old addresses (`?area=<id>` for the 9 areas, and the area paths) open the same page as before.
- **SC-004**: On the phone, 100% of the tabs and the active More rows are at least 44 px high, and no sideways scroll occurs at 320, 360, and 390 px.
- **SC-005**: The navigation has 0 text glyphs used as icons and 0 duplicate page titles on the desktop (one title for each page).
- **SC-006**: All behavior checks of the current navigation tests pass (changed only for names and layout).

## Assumptions

- No backend or API change is needed. The health call exists.
- The More rows show no live status lines and the tabs show no number badges (out of scope).
- The content of Authoring, Configuration, Gamepacks, Update, Execution, and Execution logs does not change; only the title and the navigation around them change.
- The current Notifications page title changes from "Notifications" to "Alerts", and the Monitoring page title from "Monitoring" to "Monitor".
