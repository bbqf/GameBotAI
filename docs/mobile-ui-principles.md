# Mobile UI Principles

Last reviewed: 2026-11-09 (features 139, 140, 171, 173, 184, and 185)

Each UI feature must state its **phone support level** and follow these rules. Reviewers use the checklist at the end.

## Layout mode

The UI has two layout modes. The browser media query `(max-width: 768px), (max-height: 500px)` chooses the mode.

- `phone`: the screen is 768 px wide or less, or 500 px high or less (a phone in landscape).
- `desktop`: all other screens.

Code reads the mode with `useLayoutMode()`. CSS uses the same query. The mode changes with the window size, without a reload.

## Phone support levels

| Level | Meaning | Use it when |
|-------|---------|-------------|
| `full` | Same functions as on the desktop, in a phone layout. | The area has short forms and lists. |
| `reduced` | A subset of the functions. Other functions show "Use a larger screen". | The main task is status and control. Editing needs a large screen. |
| `desktop-only` | The area shows a notice and a way back. | The area needs a large screen, a mouse, or drag and drop. |
| `hidden` | The area is not part of the phone UI. No list shows it. A phone link to it shows Queues. | The area has no use on a phone and a desktop-only notice would only add noise. |

The level is the field `phone` in `src/types/navigation.ts`. A new area cannot compile without it.

### Level of each current area

| Area | Level |
|------|-------|
| Queues | `reduced` |
| Monitoring | `full` |
| Execution | `hidden` |
| Notifications | `full` |
| Configuration | `full` |
| Update | `full` |
| Authoring | `desktop-only` |
| Execution Logs | `desktop-only` |

The value form of a parameter (feature 168) is a component, not an area. It works at 375 px: one column,
controls of at least 44 px, no horizontal scroll, and an image grid that wraps. The parameter editor
(cards, options, allowed images, preview) is desktop-only. On a phone it shows "Use a larger screen".

## Queue activity on a phone

The queue detail shows what the queue does now and what it will do next.

- The "Now" block shows the state, the running sequence (or "Waiting until <time>"), and the last result.
- The schedule list shows one row for each template entry. Enabled rows with a next run time come first, in time order.
  Enabled rows with no time come next. Disabled rows come last, marked "disabled".
- Each queue card shows one summary line. It is the same text as the Activity column of the big screen. The rule checks
  these in order: "Stopped", "Paused", "Pausing", "Running: <sequence>", "Next run at <time>", "Nothing scheduled", and
  "Waiting". When the monitor data is missing, the line shows "Running".
- The data refreshes every 5 s while the page is visible and the connection is up (`usePolling`). The refresh stops when
  the page is hidden or the connection is lost. When it starts again, one refresh runs at once.
- When a refresh fails, the last data stays on the screen with an "Old data" mark.
- The phone shows no control to create, reorder, or change the type or time of a schedule. The schedule editor stays
  `desktop-only`. The row text "Use a larger screen to edit schedules." stays.
- **Entry page** (feature 185). The parameter values of an entry have the phone support level `full`. A tap on a schedule row
  that has an entry ID opens the entry page. The row is a button of at least 44 px. A row with no entry ID is not tappable. A
  template that needs an upgrade shows the hint "Upgrade the template to open the entry settings." on each such row.
  - The page shows **Back**, the entry name, the queue name, the schedule as text with no control, and the shared parameter
    form (`ParameterBindingForm` with ad-hoc values). **Save** is the one primary action.
  - **Save** writes the values to the linked template with the call of the enable switch (`saveQueueTemplate`). A save on a
    running queue changes the template only. The notice "N changes wait in the template." shows it. One press sends one
    call. A failed save keeps the typed values and does not send the call again.
  - **Save** is off when nothing changed, while a save runs, when the connection is lost, and when the sequence did not
    load. When the sequence does not load, the page shows "The parameters did not load." and the stored values as text.
  - The page has its own history entry. The address is `?area=queues&queue=<id>&entry=<entryId>`. A reload shows the page.
    An entry that is not in the template shows "Entry not found" and a way back.
  - **Unsaved values**: **Back** and the phone back step open a dialog with **Keep editing** and **Discard changes**. A
    browser cannot cancel a back step. The app (`lib/phoneLeaveGuard.ts`) goes forward one step again and raises a counter, and
    the page opens the dialog. A tab bar move asks the same question.
  - The big screen has no entry page. The Entry settings panel of the card holds the same parameter form.
- A running queue has the controls of feature 171 (phone support level: full). The controls are **Pause**,
  **Cancel pause**, **Resume**, **Review changes** (with **Apply**), and **Upgrade template**. They work in one column, each
  control is at least 44 px high, and the text wraps with no sideways scroll. The state shows as "Pausing (waiting for the
  running sequence)" or "Paused (between sequences)". The phone view reads the health block with the detail, so it makes no
  extra request. The guide is `docs/live-template-changes.md`.
- **Review changes** is part of the notice "N changes wait in the template." (feature 173). The notice shows only when the
  template differs from the running queue. The big screen shows the same notice with the same words.
- **Run next** (feature 184, phone support level: `full`): each enabled row of a running queue that has an entry ID shows a Run next icon
  button, next to the enable switch. The button is 44 x 44 px (`IconButton` with `size="touch"`). It is on only when the state of the entry is
  `idle`. The row summary gives the state as text (`Queued to run next`, `Running`, or `Scheduled · <timing>`), so a reason never depends on a
  tooltip. One press sends one call (`POST /api/queues/{id}/run-next`), and the view reads the detail again. The view never sends the call
  again, and it turns the button off when the connection is lost. A refused call shows its text in the message area. The template editor
  stays `desktop-only`.
- A switch on a running queue changes the template only. It does not pause or apply. The notice shows the waiting change
  after the switch. The operator pauses, reviews, and applies it.

## Back navigation on a phone

- Each screen has its own history entry (`pushState`). The address holds `area`, for a queue detail `queue`, and for an entry page `entry`.
- Back and forward restore the screen of the entry. They add no entry.
- The first screen of the session has `history.state.idx` = 0. Back from it leaves the application.
- The on-screen back control of the queue detail calls `history.back()`. On the first entry it replaces the entry with the queue list.
- A phone link to a `hidden` area (`?area=execution` or `/execution`) shows Queues and replaces the entry.
- The desktop layout keeps `replaceState` only.

## Rules

1. **Layout mode.** Use `useLayoutMode()` or the phone media query. Do not detect the device from the user agent.
2. **One primary action.** Each screen has one main action. Put other actions behind a tap or in a collapsible section.
3. **Tap targets.** Each button, link, tab, and switch has a hit area of at least 44 x 44 CSS px.
4. **Input font.** A text input uses a font of at least 16 px. This stops the iOS zoom on focus.
5. **Cards, not tables.** A list shows cards (`PhoneCard`). A card has a title, an optional status chip, and at most one primary action. A tap on the card opens the detail.
6. **No hover-only control.** No function depends on hover, right-click, long press, or a keyboard shortcut. Put every `:hover` rule inside `@media (hover: hover)`.
7. **No sideways scroll.** At 320 px width and wider, `scrollWidth` is at most `clientWidth`. Cut long names with an ellipsis. Show the full name in the detail.
8. **Safe areas.** Use `env(safe-area-inset-*)` and `dvh` units so a notch or the browser bar does not cut content.
9. **Connection lost.** When the connection is lost, show a banner. Turn off the control buttons.
10. **Never repeat an action.** The UI never sends an action again after a connection loss. It only loads the data again.
11. **Desktop does not change.** Scope all phone rules to the phone query or the phone branch of the code. The desktop tests stay green without change.
12. **STE.** Write all UI text in Simplified Technical English.

## Checklist (pass or fail)

Apply this list to one screen. Mark each line pass or fail. A review takes less than 10 minutes.

- [ ] The feature states its phone support level.
- [ ] The level is set in `src/types/navigation.ts` (for an area).
- [ ] A `desktop-only` area shows the notice and never mounts its content on a phone.
- [ ] Each screen has one primary action.
- [ ] Each control is at least 44 x 44 px (check at 360 px).
- [ ] Each text input has a font of at least 16 px.
- [ ] Lists use cards, not tables.
- [ ] No control works only on hover. Every `:hover` rule is inside `@media (hover: hover)`.
- [ ] No sideways scroll at 320, 360, 390, 480, 600, and 768 px.
- [ ] The screen works in landscape (640 x 360).
- [ ] Safe areas do not cut content.
- [ ] The connection-lost banner shows and the controls turn off.
- [ ] No action is sent again after the connection returns.
- [ ] The desktop layout at 769 px and wider is unchanged.
- [ ] All new text is STE.

## Real-phone sign-off (SC-005, SC-008)

Do this check on a real phone before a release that changes the phone UI. Record the date, the phone, and the result in `specs/139-mobile-friendly-ui/baseline.md`.

1. Open the UI on the phone (see `docs/phone-access.md`). Add it to the home screen.
2. Open the UI from the home screen. Check that no browser bar shows.
3. Read the status of each queue. Start one queue, then stop it. Do not zoom.
4. Switch one entry on and off in the queue detail.
5. Open Notifications, Configuration, and Update. Do one typical task in each area.
6. Open **More**, then Authoring. Check that the notice shows and **Back to Queues** works.
7. Turn the phone to landscape. Check that no control is cut off.
8. Turn Wi-Fi off. Check that "Connection lost" shows. Turn Wi-Fi on. Check that the banner goes away.

## Related files

- `docs/ui-audit.md`: the authoring audit with a link to this checklist.
- `docs/phone-access.md`: how to reach the UI from a phone.
- `specs/139-mobile-friendly-ui/contracts/phone-ui-contract.md`: the rules that tests check.
