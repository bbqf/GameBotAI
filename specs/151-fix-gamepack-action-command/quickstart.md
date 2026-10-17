# Quickstart: Check the fix

1. Build and run the gamepack tests:
   `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Gamepack"`
2. Run the new tests first, before the fix. They must fail:
   - unit: `GamepackStepReader` converts `primitiveAction`, `commandReference`, and `commandId`.
   - contract: install of the file in `contracts\gamepack-step-json.md` keeps `primitiveAction` and `commandReference`.
3. Apply the fix. Run the same tests. They must pass.
4. Run a dry run with a step that has an unknown member. The result is not valid. The problem names the
   sequence, the step ID, and the field. The install does not create the sequence.
5. Run a dry run with `commandReference` and `primitiveAction` that name different commands. The result has a
   `structure` problem. Do the same for `action` and `primitiveAction` that differ.
6. Install a file with an Action step in a Loop `body`, and in the `body` and `elseBody` of a conditional step.
   Each step keeps its command action.
7. Install a file where the command ID is already in use by another command. The step reference, also in a
   nested step, uses the new ID.
8. Export a gamepack with a command Action step. Install it. The installed step equals the source step. Install
   it a second time. The command action stays correct.
9. Install a file with a `wait-for-image` action. Compare the result with `POST /api/sequences`. If data is
   lost, record it in a new issue. Do not fix it in this feature.
10. Open the OpenAPI document. The install description shows the Action step sample.
11. Run all gamepack tests again. Tests for conditions, loops, `breakCondition`, and `POST /api/sequences` pass
    with no change.
