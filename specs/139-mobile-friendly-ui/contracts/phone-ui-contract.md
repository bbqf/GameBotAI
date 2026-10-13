# Contract: Phone UI Behavior

**Feature**: 139-mobile-friendly-ui | **Date**: 2026-10-13

The service API does not change. This contract states the rules of the UI. Tests check each rule.

## C-01: Layout mode

- The UI uses the phone layout when `(max-width: 768px), (max-height: 500px)` matches.
- The UI changes layout when the match changes. It does not reload and it keeps the current area and form state.

## C-02: Phone shell

- The shell has a header (title of the current area), a content region, and a bottom tab bar.
- The tab bar has the items `Queues`, `Execution`, `Notifications`, and `More`. The Notifications item shows a bell icon.
- The tab bar marks the current item (`aria-current="page"`).
- The `More` sheet lists Configuration, Update, and the desktop-only areas with a "Desktop only" label.
- The tab bar stays above the phone safe area and above the on-screen keyboard rules (it hides while an input has focus).

## C-03: Default area

- With no `area` parameter in the URL, phone mode opens Queues. Desktop mode opens Authoring (no change).
- An `area` parameter always wins.

## C-04: Desktop-only notice

- In phone mode, an area with level `desktop-only` renders `DesktopOnlyNotice`.
- The notice has a heading with the area name, one sentence ("Use a larger screen for this area."), and a button "Back to Queues".
- The notice never renders the area content.
- In desktop mode, the area renders as before.

## C-05: Tap targets and inputs (phone mode, full and reduced areas)

- Every visible button, link, tab, and switch has a hit area of at least 44 x 44 CSS px.
- Text inputs use a font size of at least 16 px.
- No control works only on hover, right-click, long press, or keyboard shortcut.

## C-06: No sideways scroll

- In full and reduced areas at 320, 360, 390, 480, 600, and 768 px width, `document.documentElement.scrollWidth` is at most `clientWidth`.
- At 769 px the desktop layout applies. The same check passes for these areas.
- Long names are cut with an ellipsis. The detail view shows the full text.

## C-07: Cards

- A list in phone mode shows `PhoneCard` items. A card shows a title, an optional status chip, and at most one primary action.
- A tap on the card body opens the detail view. The detail view has a "Back" control in the header.

## C-08: Phone Queues

- A queue card shows the name, the status chip, and one primary button: `Start` when stopped, `Stop` when running.
- The queue detail shows the entries. Each entry has a switch labelled with the sequence name.
- The switch saves the linked template. The detail shows: "Changes apply when the queue starts again."
- A queue with no linked template shows the switches as read-only with the reason.
- Create, edit form, duplicate, delete, live schedule, reorder, and schedule fields are not shown. The detail shows "Use a larger screen to edit schedules."

## C-09: Connection lost

- When the connection state is `lost`, a banner shows "Connection lost" at the top of the content.
- Control buttons (`Start`, `Stop`, switches, save buttons) are disabled while the state is `lost`.
- The UI sends no queued action after the connection returns.
- When the state changes to `online`, the visible data reloads.

## C-10: Token needed

- When an API call returns 401, the UI shows "Access token needed. Open Configuration." with a button to the Configuration area.

## C-11: Install metadata

- `index.html` links `manifest.webmanifest`, sets `theme-color`, and sets the Apple standalone meta tags and the touch icon.
- `viewport` includes `viewport-fit=cover`.
- The manifest has `name`, `short_name`, `display: standalone`, `start_url`, `theme_color`, `background_color`, and icons of 192 px and 512 px (one maskable).
- The service returns the manifest with content type `application/manifest+json`.

## C-12: Desktop unchanged

- No phone rule applies when the layout mode is `desktop`.
- The existing Jest and Playwright desktop tests pass without change.
