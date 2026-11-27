# Research: Move the Monitoring area to the shared UI components

## R1. No split view on the desktop

- Decision: the desktop shows the list page or the detail page.
- Rationale: the approved mockup B and rules L7 and P1. One layout for both screens.
- Alternatives considered: keep the split view and only restyle it (does not follow mockup B; the list column was too
  narrow for the fixed columns of `ListView`).

## R2. Desktop history

- Decision: keep `replaceState` for the desktop. The back arrow clears the selection.
- Rationale: `docs/mobile-ui-principles.md` says the desktop keeps `replaceState` only. A desktop history change is
  navigation work.

## R3. The queue warning in the shared dialog

- Decision: add an optional `details?: React.ReactNode` prop to `ConfirmDialog`, rendered after the message.
- Rationale: rule D1 says one sentence for the effect; the queue warning is a second, conditional fact. A prop keeps one
  dialog design. Other dialogs do not change.
- Alternatives considered: join the two sentences into `message` (a long message; the test id and the `role="note"`
  of the warning go away).

## R4. Key-value card

- Decision: generic classes `ui-kv*` in `ui.css`, not a new component.
- Rationale: the card is markup with no logic. Classes are enough now; a component can come when a second page needs it.

## R5. Started time

- Decision: `Intl.DateTimeFormat` with `dateStyle: 'short'` and `timeStyle: 'short'`.
- Rationale: spec clarification 4. A fixed value does not make the card move.

## R6. Browser tests

- Decision: `tests/e2e/helpers/monitoringMock.ts` mocks `GET /api/monitoring/sessions` (read the exact route in
  `src/services/monitoringApi.ts`), the screenshot route (a 1 x 1 PNG), and the restart routes. Both browser test files
  use it.
