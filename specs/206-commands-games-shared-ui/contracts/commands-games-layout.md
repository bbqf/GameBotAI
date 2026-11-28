# Contract: Commands and Games layout (feature 206)

Desktop, 1280 px. The browser tests and the unit tests check these points.

| ID | Check |
|----|-------|
| C-01 | Games: the page header "Games" has one primary button "Create game". The filter input "Filter by name" is above the list, not inside a `th`. |
| C-02 | Games list: the Package name column is 320 px (±1); each row is 56 px (±1); a game with no package name shows "—". |
| C-03 | Commands list: columns Name and Steps (96 px ±1); each row is 56 px (±1); no column "Game" and no Game select. |
| C-04 | Each row has the button "Edit <name>" and the menu button "More actions for <name>" with the item "Delete" last. |
| C-05 | A row click opens the edit page: a "Back" button and the heading with the item name. |
| C-06 | The form (`.ui-form`) is 720 px wide (±1) and centered (left and right space equal ±2). |
| C-07 | The footer bar is visible in the viewport. Edit mode: "Delete" on the left; "Cancel" then "Save" on the right; Save is the last button. Create mode: no Delete. |
| C-08 | Delete from the row menu opens the dialog "Delete game <name>?" / "Delete command <name>?" with "You cannot undo this.". |
| C-09 | A 409 answer: the dialog stays open, shows "Referenced by:" and the names, and its Delete button is off; Cancel closes it. |
| C-10 | "Back" on the form page shows the list. |
| C-11 | A successful delete shows "Game deleted." / "Command deleted." in the message area. |
| C-12 | Create mode titles: "New game" / "New command". |
| C-13 | At 1024 x 768, the lists and the form pages have no sideways scroll (`scrollWidth <= clientWidth`). |
| C-14 | A game with a name of 200 characters: its row is 56 px (±1), and its edit page title is one line (height of the title less than 2 lines). |
