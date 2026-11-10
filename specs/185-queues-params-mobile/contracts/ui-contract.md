# UI Contract: Entry Settings and Phone Entry Page

The feature has no service contract. The calls that exist do not change. This file lists the rules that tests check.

## Big screen: entry card (desktop mode)

| ID | Rule |
|----|------|
| D-01 | The main row of a card holds these items in this order: drag handle, enable switch, name with badges, schedule summary, Run next, settings button, Remove. Run next shows only when the editor gives the support. The row holds no schedule select and no timer input. |
| D-02 | The settings button has the name `Entry settings for <name>`, a title, no text, and `aria-expanded`. |
| D-03 | A new card is closed. The button toggles only its own card. |
| D-04 | The open panel holds the Schedule part first. It has the schedule type select. For a Timer entry it also has the timer mode select and the time input or the h, m, s offset inputs. The labels of these controls stay as they are today (`Schedule type for <name>` and so on). |
| D-05 | The Parameters part follows the Schedule part. It shows when the sequence declares parameters or the entry has values. Otherwise the panel shows the button `Add a value` below the Schedule part. The button shows the ad-hoc add field. |
| D-06 | A change of the schedule type moves the card to the matching scheduling area. The panel of the card stays open. |
| D-07 | A drag of a card does not close its panel. When an entry is removed, its open state is removed. |
| D-08 | The `Parameters` badge shows on a closed card that has values. |
| D-09 | The summary text equals `scheduleSummary(entry)`. It follows the timer mode. It changes at once when a schedule control changes. |
| D-10 | When the editor is `disabled`, the controls in the panel are disabled. The settings button stays on, so the operator can read the values. |
| D-11 | A closed card at 1024 px is at most 57 px high for a name of up to 30 characters (Playwright). |
| D-12 | The main row has at most 5 controls: drag handle, enable switch, Run next, settings button, and Remove. |

## Phone: queue detail (phone mode)

| ID | Rule |
|----|------|
| P-01 | A schedule row with an entry ID is a tap target of at least 44 px. A tap opens the entry page. |
| P-02 | The enable switch and Run next keep their behavior. A tap on them does not open the page. |
| P-03 | A row with no entry ID is not tappable. A template can need an upgrade. The row then shows the hint `Upgrade the template to open the entry settings.` When no template is linked, the existing text about the missing template is the only hint. |
| P-04 | The text `Use a larger screen to edit schedules.` stays. |
| P-05 | The row text (name and timing) does not change. |

## Phone: entry page

| ID | Rule |
|----|------|
| E-01 | The page shows a Back button, the entry name, the queue name, the schedule as text (read-only), and the parameter form. |
| E-02 | The page has one primary action: `Save`. Save is off when there is nothing to save, while a save runs, when the connection is lost, and when the sequence did not load. |
| E-03 | The form is the shared `ParameterBindingForm` with `allowAdHoc`. Each control is at least 44 px high. Each text input uses at least 16 px. |
| E-04 | A sequence with no declaration and no value shows `This sequence has no parameters.` The button `Add a value` shows the ad-hoc add field. |
| E-05 | A successful save shows `Saved.` in a status area. A failed save shows the error text in an alert area and keeps the values. One press sends one call. No call repeats. |
| E-06 | An address of a queue with no linked template shows `Entry not found` and a way back. An address of an entry that is not in the template does the same. |
| E-07 | A save on a running queue changes the template only. After Back, the queue detail shows the notice of waiting changes. |
| E-08 | Back with unsaved edits opens a dialog with `Keep editing` and `Discard changes`. The phone back step does the same (leave guard). |
| E-09 | The connection-lost banner shows (it is part of the app frame). Save is off. The page does not send a save when the connection returns. |
| E-10 | The page has no sideways scroll at 320, 360, 390, 480, 600, and 768 px, and at 640 x 360. |
| E-11 | The address `?area=queues&queue=<id>&entry=<entryId>` opens the page after a reload. A missing queue or entry shows a message and a way back. |
| E-12 | When the sequence does not load, the page shows `The parameters did not load.` and the stored values as plain text. Save is off. |

## Mode rules

| ID | Rule |
|----|------|
| M-01 | The entry page mounts only in phone mode. The inline panel mounts only in desktop mode. |
| M-02 | When the window changes between the modes, the selected queue stays. A phone address with an entry shows the queue editor in desktop mode. The desktop UI has no entry address. |
