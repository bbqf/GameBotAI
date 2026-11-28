# Research: Move the Sequences list, form page, and delete to the shared UI components

## R1. Merge the two forms first by a written diff

- **Decision**: Before the merge, list each difference between the create form and the edit form in the implementation notes. Keep each real difference by a mode condition.
- **Rationale**: The two copies have grown apart over many features (078, 122, 170, 175, 189). A merge that drops a difference changes behavior with no test failure if no test covers it.

## R2. One set of ids

- **Decision**: Both modes use the create ids.
- **Rationale**: One form has one set of ids. Only labels and tests use them. The tests change (FR-021).

## R3. Options column as text

- **Decision**: The options show as muted text separated by ", ", not as badges.
- **Rationale**: Rule L3 (one line, "…"), rule L8 ("—"). Badges in a 360 px cell wrap when both options are on.

## R4. "Step through" in the header

- **Decision**: A secondary header button; the reason in the message area.
- **Rationale**: Rule H1 (other page actions are secondary buttons in the header). The footer bar holds only the form actions (rule F4).

## R5. 880 px form

- **Decision**: A modifier `ui-form--wide` of the card form.
- **Rationale**: Guideline section 2 gives the step editor a max. width of 880 px. The Sequences form holds the step editor.
