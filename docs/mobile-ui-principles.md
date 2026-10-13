# Mobile UI Principles

Last reviewed: 2026-10-13 (feature 139)

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

The level is the field `phone` in `src/types/navigation.ts`. A new area cannot compile without it.

### Level of each current area

| Area | Level |
|------|-------|
| Queues | `reduced` |
| Execution | `full` |
| Notifications | `full` |
| Configuration | `full` |
| Update | `full` |
| Authoring | `desktop-only` |
| Execution Logs | `desktop-only` |

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
5. Open Execution, Notifications, Configuration, and Update. Do one typical task in each area.
6. Open **More**, then Authoring. Check that the notice shows and **Back to Queues** works.
7. Turn the phone to landscape. Check that no control is cut off.
8. Turn Wi-Fi off. Check that "Connection lost" shows. Turn Wi-Fi on. Check that the banner goes away.

## Related files

- `docs/ui-audit.md`: the authoring audit with a link to this checklist.
- `docs/phone-access.md`: how to reach the UI from a phone.
- `specs/139-mobile-friendly-ui/contracts/phone-ui-contract.md`: the rules that tests check.
