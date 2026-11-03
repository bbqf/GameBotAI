# UI Contract: Parameter row box

For each declared (not ad-hoc) row, when the form is not in preview mode:

- A checkbox shows with the visible label "Use default".
- The checkbox accessible name is `Use default <name>`. It does not include the default text.
- Next to the label, a text shows the default value:
  - "No default" when the parameter has no default (null or undefined).
  - "(empty)" when the default is an empty string.
  - The option label for a choice default.
  - The image id for an image default.
  - The raw value for a number or text default.
- The checkbox is always shown, also when there is no default.
- The word "Inherit" does not show in the form.
- The text wraps on a narrow screen and does not break the row.

Used in: queue template entry, run request, step binding. Preview mode and ad-hoc rows show no box.
