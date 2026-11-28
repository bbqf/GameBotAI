# Contract: Sequences layout (feature 207)

Desktop, 1280 px, unless a row says other.

| ID | Check |
|----|-------|
| S-01 | The page header "Sequences" has one primary button "Create sequence". The filter "Filter by name" is above the list, not inside a `th`. |
| S-02 | The list "Sequences table" has the columns Name, Options (360 px ±1), Steps (96 px ±1); each row is 56 px (±1). |
| S-03 | A sequence with both options shows "No success notifications, Ignores smart scheduling" in one line; with none, "—". |
| S-04 | Each row has "Edit <name>" and "More actions for <name>" with "Delete" last. |
| S-05 | A row click opens the edit page: a "Back" button, the heading with the name, and the button "Step through" in the header. |
| S-06 | The form (`.ui-form--wide`) is 880 px (±1) and centered (left and right space equal ±2). |
| S-07 | The footer bar is in the viewport. Edit mode: "Delete" on the left; "Cancel" then "Save" on the right; Save is last. Create mode: no Delete; title "New sequence". |
| S-08 | Delete from the row menu opens "Delete sequence <name>?" with "You cannot undo this.". |
| S-09 | A 409: the dialog shows "Referenced by:" and the names; its Delete is off; Cancel closes it. |
| S-10 | "Back" shows the list. |
| S-11 | At 1024 x 768 the list and the form page have no sideways scroll. |
| S-12 | The text "No sequences yet" does not show. |
