# Quickstart: List-valued parameters

1. Declare a parameter with kind `image`, `multiple: true`, and `allowedImages`. Save the sequence through the API or the web UI.
2. Open a queue template entry for the sequence. The value form shows the allowed icons.
3. Select two icons. Save. Open the entry again. Check that both icons stay selected, in the order of selection.
4. Call `GET` for the template. Check that the binding has `values` with two items and `value` with the joined text.
5. Use `{{quests}}` in a text field. Run. Check that the field gets `quest_b|quest_a`.
6. Use `{{quests}}` as the image field of a step. Run with two items. Check that the step fails with a message that names the parameter.
7. Save an empty list for a required parameter. Check that the API refuses it.

Run the tests:

- Domain and Service: `dotnet test` for the solution.
- Web UI: `npm test` in `src/web-ui`.
